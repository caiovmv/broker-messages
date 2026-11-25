using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace broker.Correio;

public sealed record ItemMailgrid(string status, string codigo, string? criptokey, string? to);

public sealed record LinhaDeRelatorio(string msgid, string email_de, string email_para, string data, string hora, string status);

public sealed class ServicoDeCorreio
{
    private readonly IAcervo _acervo;
    private readonly IProtecaoDeSegredo _protecao;
    private readonly IDurabilidade _durabilidade;
    private readonly string _hostSmtp;
    private readonly int _portaSmtp;

    public ServicoDeCorreio(IAcervo acervo, IProtecaoDeSegredo protecao, IDurabilidade durabilidade, string hostSmtp, int portaSmtp)
    {
        _acervo = acervo;
        _protecao = protecao;
        _durabilidade = durabilidade;
        _hostSmtp = hostSmtp;
        _portaSmtp = portaSmtp;
    }

    public string HostSmtp => _hostSmtp;

    public int PortaSmtp => _portaSmtp;

    public async Task<IReadOnlyList<ItemMailgrid>> EnviarEmailAsync(JsonElement corpo, bool exigirHost, CancellationToken cancellationToken)
    {
        var host = Texto(corpo, "host_smtp");
        var usuario = Texto(corpo, "usuario_smtp");
        var senha = Texto(corpo, "senha_smtp");
        var remetente = Texto(corpo, "emailRemetente");
        var assunto = Texto(corpo, "assunto");
        var mensagem = Texto(corpo, "mensagem");
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha) || string.IsNullOrWhiteSpace(remetente)
            || string.IsNullOrWhiteSpace(assunto) || string.IsNullOrWhiteSpace(mensagem) || (exigirHost && string.IsNullOrWhiteSpace(host)))
        {
            return Falha("208", "FALTAM PARÂMETROS");
        }

        if (exigirHost && !host!.Equals(_hostSmtp, StringComparison.OrdinalIgnoreCase))
        {
            return Falha("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        var aplicativo = await AutenticarSenhaAsync(usuario, senha, cancellationToken);
        if (aplicativo is null)
        {
            return Falha("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        var tipo = Texto(corpo, "mensagemTipo");
        if (tipo is not null && tipo is not "html" and not "text")
        {
            return Falha("212", "ERRO: TIPO INVÁLIDO");
        }

        var encoding = Texto(corpo, "mensagemEncoding");
        var decodificada = Decodificar(mensagem, encoding);
        if (decodificada is null)
        {
            return Falha("211", "ERRO: ENCODING INVÁLIDO");
        }

        if (!Lista(corpo, "emailDestino", out var destinos, out var erroDestino))
        {
            return Falha(erroDestino ?? "227", TextoDoCodigo(erroDestino));
        }

        if (destinos.Count == 0)
        {
            return Falha("208", "FALTAM PARÂMETROS");
        }

        if (!Lista(corpo, "emailDestinoCopia", out var copias, out var erroCopia))
        {
            return Falha(erroCopia ?? "228", TextoDoCodigo(erroCopia));
        }

        if (!Lista(corpo, "emailDestinoCopiaOculta", out var ocultas, out var erroOculta))
        {
            return Falha(erroOculta ?? "229", TextoDoCodigo(erroOculta));
        }

        var anexos = LerAnexos(corpo);
        if (Encoding.UTF8.GetByteCount(decodificada) + anexos.Sum(item => item.ConteudoBase64.Length) > 10 * 1024 * 1024)
        {
            return Falha("215", "ERRO: TAMANHO EXCEDIDO");
        }

        var variaveis = Variaveis(corpo);
        var agora = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var mensagemId = Guid.NewGuid().ToString("N");
        var gravada = new MensagemGravada
        {
            Id = mensagemId,
            AplicativoId = aplicativo.Id,
            Canal = "email",
            MsgId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
            Remetente = remetente,
            NomeRemetente = Texto(corpo, "nomeRemetente") ?? "",
            Assunto = assunto,
            Corpo = decodificada,
            Anexos = JsonSerializer.Serialize(anexos),
            CriadoEm = agora,
        };
        var entregas = new List<EntregaGravada>();
        AdicionarEntregas(entregas, mensagemId, gravada, destinos, variaveis, "to", agora);
        AdicionarEntregas(entregas, mensagemId, gravada, copias, null, "cc", agora);
        AdicionarEntregas(entregas, mensagemId, gravada, ocultas, null, "bcc", agora);
        await _acervo.InserirMensagemAsync(gravada, entregas, cancellationToken);
        await _durabilidade.EnviarEEsperarAsync(mensagemId, cancellationToken);
        var finais = await _acervo.EntregasDaMensagemAsync(mensagemId, cancellationToken);
        return finais.Select(item => new ItemMailgrid(
            item.Codigo == "200" ? "MSG ENVIADA" : "ERRO DE ENVIO",
            item.Codigo,
            item.Id,
            item.Destinatario)).ToArray();
    }

    public async Task<ItemMailgrid> EnviarSmsAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var token = Texto(corpo, "token_auth");
        var para = Texto(corpo, "para");
        var texto = Texto(corpo, "texto");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(para) || string.IsNullOrWhiteSpace(texto))
        {
            return new ItemMailgrid("FALTAM PARÂMETROS", "208", null, para);
        }

        var aplicativo = await AutenticarTokenAsync(token, cancellationToken);
        if (aplicativo is null)
        {
            return new ItemMailgrid("ERRO: FALHA DE AUTENTICAÇÃO", "207", null, para);
        }

        var agora = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var mensagemId = Guid.NewGuid().ToString("N");
        var entregaId = Guid.NewGuid().ToString("N");
        var gravada = new MensagemGravada
        {
            Id = mensagemId,
            AplicativoId = aplicativo.Id,
            Canal = "sms",
            MsgId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
            Remetente = "",
            Assunto = "",
            Corpo = texto,
            CriadoEm = agora,
        };
        var entrega = new EntregaGravada
        {
            Id = entregaId,
            MensagemId = mensagemId,
            Destinatario = para,
            Papel = "to",
            Codigo = "",
            Status = "pendente",
            AtualizadoEm = agora,
            CorpoPersonalizado = texto,
        };
        await _acervo.InserirMensagemAsync(gravada, new[] { entrega }, cancellationToken);
        await _durabilidade.EnviarEEsperarAsync(mensagemId, cancellationToken);
        var finais = await _acervo.EntregasDaMensagemAsync(mensagemId, cancellationToken);
        var final = finais[0];
        return new ItemMailgrid(final.Codigo == "200" ? "MSG ENVIADA" : "ERRO DE ENVIO", final.Codigo, gravada.MsgId, para);
    }

    public async Task<object> RelatorioAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var aplicativo = await AutenticarLeituraAsync(corpo, cancellationToken);
        if (aplicativo is null)
        {
            return Falha("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        if (!Periodo(corpo, out var inicio, out var fim))
        {
            return Falha("208", "FALTAM PARÂMETROS");
        }

        var status = Texto(corpo, "status");
        var destino = Texto(corpo, "emaildestino");
        var remetente = Texto(corpo, "remetente");
        var idMensagem = Texto(corpo, "idMensagem");
        var linhas = await FiltrarAsync(aplicativo.Id, inicio, fim, status, destino, remetente, idMensagem, cancellationToken);
        var mapa = new Dictionary<string, LinhaDeRelatorio>();
        for (var i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            var quando = DateTimeOffset.Parse(linha.Quando, null, System.Globalization.DateTimeStyles.RoundtripKind);
            mapa[i.ToString()] = new LinhaDeRelatorio(
                linha.MsgId,
                linha.Remetente,
                linha.Destinatario,
                quando.ToString("dd/MM/yyyy"),
                quando.ToString("HH:mm:ss"),
                linha.Status);
        }

        return mapa;
    }

    public async Task<object> ConsolidarAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var aplicativo = await AutenticarLeituraAsync(corpo, cancellationToken);
        if (aplicativo is null)
        {
            return Falha("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        if (!Periodo(corpo, out var inicio, out var fim))
        {
            return Falha("208", "FALTAM PARÂMETROS");
        }

        var status = Texto(corpo, "status");
        var destino = Texto(corpo, "emaildestino");
        var remetente = Texto(corpo, "remetente");
        var linhas = await FiltrarAsync(aplicativo.Id, inicio, fim, status, destino, remetente, null, cancellationToken);
        var total = linhas.Count;
        var ok = linhas.Count(item => item.Codigo == "200");
        var erro = total - ok;
        var sucesso = total == 0 ? 0m : Math.Round(ok * 100m / total, 2);
        var falha = total == 0 ? 0m : Math.Round(erro * 100m / total, 2);
        return new[]
        {
            new Dictionary<string, string>
            {
                ["Periodo consultado"] = "De " + inicio.ToString("yyyy-MM-dd HH:mm:ss") + " ate " + fim.ToString("yyyy-MM-dd HH:mm:ss"),
                ["Total enviado"] = total.ToString(),
                ["Entregues com sucesso"] = ok.ToString(),
                ["Devolvidos com erro"] = erro.ToString(),
                ["% sucesso"] = sucesso.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                ["% erro"] = falha.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            },
        };
    }

    public async Task<object> AdicionarDominioAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var aplicativo = await AutenticarTokenDoCorpoAsync(corpo, cancellationToken);
        if (aplicativo is null)
        {
            return Um("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        var nome = Texto(corpo, "dominio");
        if (string.IsNullOrWhiteSpace(nome) || !nome.Contains('.'))
        {
            return Um("208", "FALTAM PARÂMETROS");
        }

        nome = nome.Trim().ToLowerInvariant();
        var existente = await _acervo.DominioAsync(aplicativo.Id, nome, cancellationToken);
        if (existente is not null)
        {
            return Um("208", "DOMÍNIO JÁ CADASTRADO");
        }

        var dominio = NovoDominio(aplicativo.Id, nome);
        await _acervo.InserirDominioAsync(dominio, cancellationToken);
        await _durabilidade.DispararVerificacaoAsync(dominio.Id, cancellationToken);
        return Um("200", "DOMÍNIO ADICIONADO COM SUCESSO");
    }

    public async Task<object> RemoverDominioAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var aplicativo = await AutenticarTokenDoCorpoAsync(corpo, cancellationToken);
        if (aplicativo is null)
        {
            return Um("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        var nome = Texto(corpo, "dominio");
        if (string.IsNullOrWhiteSpace(nome))
        {
            return Um("208", "FALTAM PARÂMETROS");
        }

        var dominio = await _acervo.DominioAsync(aplicativo.Id, nome.Trim().ToLowerInvariant(), cancellationToken);
        if (dominio is null)
        {
            return Um("208", "DOMÍNIO NÃO ENCONTRADO");
        }

        await _acervo.RemoverDominioAsync(dominio.Id, cancellationToken);
        return Um("200", "DOMINIO REMOVIDO COM SUCESSO");
    }

    public async Task<object> ChecarDominioAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var aplicativo = await AutenticarTokenDoCorpoAsync(corpo, cancellationToken);
        if (aplicativo is null)
        {
            return Um("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        var nome = Texto(corpo, "dominio");
        if (string.IsNullOrWhiteSpace(nome))
        {
            return Um("208", "FALTAM PARÂMETROS");
        }

        var dominio = await _acervo.DominioAsync(aplicativo.Id, nome.Trim().ToLowerInvariant(), cancellationToken);
        if (dominio is null)
        {
            return Um("208", "DOMÍNIO NÃO ENCONTRADO");
        }

        return FormaDeChecagem(dominio);
    }

    public async Task<object> ListarDominiosAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var aplicativo = await AutenticarTokenDoCorpoAsync(corpo, cancellationToken);
        if (aplicativo is null)
        {
            return Um("207", "ERRO: FALHA DE AUTENTICAÇÃO");
        }

        var dominios = await _acervo.DominiosAsync(aplicativo.Id, cancellationToken);
        if (dominios.Count == 1)
        {
            return FormaDeLista(dominios[0]);
        }

        var mapa = new Dictionary<string, object>();
        for (var i = 0; i < dominios.Count; i++)
        {
            mapa[i.ToString()] = FormaDeLista(dominios[i]);
        }

        return mapa;
    }

    public async Task<AplicativoVisivel> CriarAplicativoAsync(string nome, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new Common.ExcecaoDeDominio(Common.Erro.Validacao("Nome e obrigatorio."));
        }

        var senha = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var aplicativo = new Aplicativo
        {
            Id = Guid.NewGuid().ToString("N"),
            Nome = nome.Trim(),
            UsuarioSmtp = "app-" + Guid.NewGuid().ToString("N")[..12],
            SenhaProtegida = _protecao.Proteger(senha),
            TokenBusca = ResumoDeSegredo.De(token),
            TokenProtegido = _protecao.Proteger(token),
            CriadoEm = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        await _acervo.InserirAplicativoAsync(aplicativo, cancellationToken);
        return Visivel(aplicativo, senha, token);
    }

    public async Task<IReadOnlyList<AplicativoVisivel>> AplicativosAsync(CancellationToken cancellationToken)
    {
        var lista = await _acervo.AplicativosAsync(cancellationToken);
        return lista.Select(item => Visivel(item, _protecao.Revelar(item.SenhaProtegida), _protecao.Revelar(item.TokenProtegido))).ToArray();
    }

    public async Task<AplicativoVisivel> RotacionarAsync(string id, CancellationToken cancellationToken)
    {
        var aplicativo = await _acervo.AplicativoPorIdAsync(id, cancellationToken);
        if (aplicativo is null)
        {
            throw new Common.ExcecaoDeDominio(Common.Erro.NaoEncontrado("Aplicativo"));
        }

        var senha = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var busca = ResumoDeSegredo.De(token);
        await _acervo.AtualizarSegredosAsync(id, _protecao.Proteger(senha), busca, _protecao.Proteger(token), cancellationToken);
        aplicativo.SenhaProtegida = _protecao.Proteger(senha);
        aplicativo.TokenProtegido = _protecao.Proteger(token);
        return Visivel(aplicativo, senha, token);
    }

    public async Task<IReadOnlyList<DestinoVisivel>> DestinosVisiveisAsync(string aplicativoId, string? canal, CancellationToken cancellationToken)
    {
        var lista = await _acervo.DestinosAsync(aplicativoId, canal, cancellationToken);
        return lista.Select(item => new DestinoVisivel(item.Id, item.AplicativoId, item.Canal, item.Provedor, item.Ordem, item.Nome)).ToArray();
    }

    public async Task<DestinoVisivel> CriarDestinoAsync(string aplicativoId, string canal, string provedor, string nome, string segredo, CancellationToken cancellationToken)
    {
        if (canal is not "email" and not "sms" || string.IsNullOrWhiteSpace(provedor) || string.IsNullOrWhiteSpace(nome))
        {
            throw new Common.ExcecaoDeDominio(Common.Erro.Validacao("Destino incompleto."));
        }

        var atuais = await _acervo.DestinosAsync(aplicativoId, canal, cancellationToken);
        var destino = new Destino
        {
            Id = Guid.NewGuid().ToString("N"),
            AplicativoId = aplicativoId,
            Canal = canal,
            Provedor = provedor.Trim().ToLowerInvariant(),
            Ordem = atuais.Count == 0 ? 1 : atuais.Max(item => item.Ordem) + 1,
            Nome = nome.Trim(),
            SegredoProtegido = string.IsNullOrEmpty(segredo) ? "" : _protecao.Proteger(segredo),
        };
        await _acervo.InserirDestinoAsync(destino, cancellationToken);
        return new DestinoVisivel(destino.Id, destino.AplicativoId, destino.Canal, destino.Provedor, destino.Ordem, destino.Nome);
    }

    public Task ReordenarAsync(string aplicativoId, string canal, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        return _acervo.ReordenarDestinosAsync(aplicativoId, canal, ids, cancellationToken);
    }

    public Task RemoverDestinoAsync(string id, CancellationToken cancellationToken)
    {
        return _acervo.RemoverDestinoAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<object>> DominiosDoConsoleAsync(string aplicativoId, CancellationToken cancellationToken)
    {
        var lista = await _acervo.DominiosAsync(aplicativoId, cancellationToken);
        return lista.Select(FormaDeChecagem).ToArray();
    }

    public async Task<IReadOnlyList<EntregaConsulta>> EnviosAsync(string aplicativoId, CancellationToken cancellationToken)
    {
        return await _acervo.ConsultarAsync(aplicativoId, cancellationToken);
    }

    public async Task AtualizarPorProvedorAsync(string? entregaId, string? idProvedor, string status, CancellationToken cancellationToken)
    {
        EntregaGravada? entrega = null;
        if (!string.IsNullOrWhiteSpace(entregaId))
        {
            entrega = await _acervo.EntregaPorIdAsync(entregaId, cancellationToken);
        }

        if (entrega is null && !string.IsNullOrWhiteSpace(idProvedor))
        {
            entrega = await _acervo.EntregaPorProvedorAsync(idProvedor, cancellationToken);
        }

        if (entrega is null)
        {
            return;
        }

        var codigo = status.Contains("sucesso", StringComparison.OrdinalIgnoreCase) || status.Contains("delivered", StringComparison.OrdinalIgnoreCase)
            ? "200"
            : "204";
        var texto = status.Contains("hard", StringComparison.OrdinalIgnoreCase)
            ? "hard bounce"
            : status.Contains("soft", StringComparison.OrdinalIgnoreCase) || status.Contains("deferred", StringComparison.OrdinalIgnoreCase)
                ? "soft bounce"
                : codigo == "200" ? "Entregue com sucesso" : "ERRO DE ENVIO";
        await _acervo.AtualizarEntregaAsync(entrega.Id, codigo, texto, entrega.Provedor, entrega.IdProvedor, cancellationToken);
    }

    private async Task<IReadOnlyList<EntregaConsulta>> FiltrarAsync(
        string aplicativoId,
        DateTimeOffset inicio,
        DateTimeOffset fim,
        string? status,
        string? destino,
        string? remetente,
        string? idMensagem,
        CancellationToken cancellationToken)
    {
        var linhas = await _acervo.ConsultarAsync(aplicativoId, cancellationToken);
        return linhas.Where(item =>
        {
            var quando = DateTimeOffset.Parse(item.Quando, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (quando < inicio || quando > fim)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(destino) && !item.Destinatario.Equals(destino, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(remetente) && !item.Remetente.Equals(remetente, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(idMensagem) && !item.MsgId.Equals(idMensagem, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                return true;
            }

            return status.ToLowerInvariant() switch
            {
                "sucesso" => item.Codigo == "200",
                "erro" => item.Codigo != "200",
                "soft" => item.Status.Contains("soft", StringComparison.OrdinalIgnoreCase),
                "hard" => item.Status.Contains("hard", StringComparison.OrdinalIgnoreCase),
                _ => true,
            };
        }).ToArray();
    }

    private DominioRemetente NovoDominio(string aplicativoId, string nome)
    {
        using var rsa = RSA.Create(2048);
        var publica = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var chave = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return new DominioRemetente
        {
            Id = Guid.NewGuid().ToString("N"),
            AplicativoId = aplicativoId,
            Nome = nome,
            SpfNome = nome,
            SpfValor = "v=spf1 include:" + _hostSmtp + " -all",
            DkimNome = "default._domainkey." + nome,
            DkimValor = "v=DKIM1; k=rsa; p=" + publica,
            ChaveNome = "_broker." + nome,
            ChaveValor = chave,
        };
    }

    private static object FormaDeChecagem(DominioRemetente dominio)
    {
        return new
        {
            dominio = dominio.Nome,
            validado = dominio.Validado,
            liberado = dominio.Liberado,
            dkim = new[] { new { name = dominio.DkimNome, type = "TXT", value = dominio.DkimValor } },
            spf = new[] { new { name = dominio.SpfNome, type = "TXT", value = dominio.SpfValor } },
            chave = new[] { new { name = dominio.ChaveNome, type = "TXT", value = dominio.ChaveValor } },
        };
    }

    private static Dictionary<string, object> FormaDeLista(DominioRemetente dominio)
    {
        return new Dictionary<string, object>
        {
            ["dominio"] = dominio.Nome,
            ["validado"] = dominio.Validado,
            ["liberado para envios"] = dominio.Liberado,
            ["dkim"] = new[] { new { name = dominio.DkimNome, type = "TXT", value = dominio.DkimValor } },
            ["spf"] = new[] { new { name = dominio.SpfNome, type = "TXT", value = dominio.SpfValor } },
            ["chave"] = new[] { new { name = dominio.ChaveNome, type = "TXT", value = dominio.ChaveValor } },
        };
    }

    private AplicativoVisivel Visivel(Aplicativo aplicativo, string senha, string token)
    {
        return new AplicativoVisivel(
            aplicativo.Id,
            aplicativo.Nome,
            _hostSmtp,
            _portaSmtp,
            aplicativo.UsuarioSmtp,
            senha,
            token,
            aplicativo.CriadoEm);
    }

    private async Task<Aplicativo?> AutenticarLeituraAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var token = Texto(corpo, "token_auth");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return await AutenticarTokenAsync(token, cancellationToken);
        }

        var usuario = Texto(corpo, "usuario_smtp");
        var senha = Texto(corpo, "senha_smtp");
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha))
        {
            return null;
        }

        return await AutenticarSenhaAsync(usuario, senha, cancellationToken);
    }

    private Task<Aplicativo?> AutenticarTokenDoCorpoAsync(JsonElement corpo, CancellationToken cancellationToken)
    {
        var token = Texto(corpo, "token_auth");
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<Aplicativo?>(null);
        }

        return AutenticarTokenAsync(token, cancellationToken);
    }

    private async Task<Aplicativo?> AutenticarTokenAsync(string token, CancellationToken cancellationToken)
    {
        return await _acervo.AplicativoPorTokenAsync(ResumoDeSegredo.De(token), cancellationToken);
    }

    private async Task<Aplicativo?> AutenticarSenhaAsync(string usuario, string senha, CancellationToken cancellationToken)
    {
        var aplicativo = await _acervo.AplicativoPorUsuarioAsync(usuario.Trim(), cancellationToken);
        if (aplicativo is null)
        {
            return null;
        }

        var guardada = _protecao.Revelar(aplicativo.SenhaProtegida);
        return ResumoDeSegredo.Iguais(guardada, senha) ? aplicativo : null;
    }

    private static void AdicionarEntregas(
        List<EntregaGravada> entregas,
        string mensagemId,
        MensagemGravada mensagem,
        IReadOnlyList<string> enderecos,
        JsonElement? variaveis,
        string papel,
        string agora)
    {
        for (var i = 0; i < enderecos.Count; i++)
        {
            var assunto = mensagem.Assunto;
            var corpo = mensagem.Corpo;
            if (variaveis is { ValueKind: JsonValueKind.Array } && i < variaveis.Value.GetArrayLength())
            {
                var item = variaveis.Value[i];
                assunto = AplicarVariaveis(assunto, item);
                corpo = AplicarVariaveis(corpo, item);
            }

            entregas.Add(new EntregaGravada
            {
                Id = Guid.NewGuid().ToString("N"),
                MensagemId = mensagemId,
                Destinatario = enderecos[i],
                Papel = papel,
                Codigo = "",
                Status = "pendente",
                AtualizadoEm = agora,
                AssuntoPersonalizado = assunto,
                CorpoPersonalizado = corpo,
            });
        }
    }

    private static string AplicarVariaveis(string texto, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return texto;
        }

        foreach (var propriedade in item.EnumerateObject())
        {
            if (propriedade.Value.ValueKind == JsonValueKind.String)
            {
                texto = texto.Replace("{{" + propriedade.Name + "}}", propriedade.Value.GetString(), StringComparison.Ordinal);
            }
        }

        return texto;
    }

    private static List<AnexoDeEnvio> LerAnexos(JsonElement corpo)
    {
        if (!corpo.TryGetProperty("mensagemAnexos", out var anexos) || anexos.ValueKind != JsonValueKind.Array)
        {
            return new List<AnexoDeEnvio>();
        }

        var lista = new List<AnexoDeEnvio>();
        foreach (var item in anexos.EnumerateArray())
        {
            lista.Add(new AnexoDeEnvio(
                item.TryGetProperty("name", out var nome) ? nome.GetString() ?? "anexo" : "anexo",
                item.TryGetProperty("type", out var tipo) ? tipo.GetString() ?? "application/octet-stream" : "application/octet-stream",
                item.TryGetProperty("content", out var conteudo) ? conteudo.GetString() ?? "" : ""));
        }

        return lista;
    }

    private static JsonElement? Variaveis(JsonElement corpo)
    {
        if (corpo.TryGetProperty("variaveis", out var valor))
        {
            return valor;
        }

        return null;
    }

    private static bool Lista(JsonElement corpo, string nome, out List<string> enderecos, out string? codigoErro)
    {
        enderecos = new List<string>();
        codigoErro = null;
        if (!corpo.TryGetProperty(nome, out var valor))
        {
            if (nome != "emailDestino")
            {
                return true;
            }

            codigoErro = "208";
            return false;
        }

        if (valor.ValueKind != JsonValueKind.Array)
        {
            codigoErro = nome switch
            {
                "emailDestinoCopia" => "228",
                "emailDestinoCopiaOculta" => "229",
                _ => "227",
            };
            return false;
        }

        foreach (var item in valor.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var texto = item.GetString();
            if (!string.IsNullOrWhiteSpace(texto))
            {
                enderecos.Add(texto.Trim());
            }
        }

        var limite = nome switch
        {
            "emailDestinoCopia" => "222",
            "emailDestinoCopiaOculta" => "223",
            _ => "221",
        };
        if (enderecos.Count > 100)
        {
            codigoErro = limite;
            return false;
        }

        return true;
    }

    private static string? Decodificar(string mensagem, string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return mensagem;
        }

        if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(mensagem));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        if (encoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return QuotedPrintable(mensagem);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static string QuotedPrintable(string texto)
    {
        var limpo = texto.Replace("=\r\n", "", StringComparison.Ordinal).Replace("=\n", "", StringComparison.Ordinal);
        var bytes = new List<byte>(limpo.Length);
        for (var i = 0; i < limpo.Length; i++)
        {
            if (limpo[i] == '=' && i + 2 < limpo.Length)
            {
                bytes.Add(Convert.ToByte(limpo.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            bytes.Add((byte)limpo[i]);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool Periodo(JsonElement corpo, out DateTimeOffset inicio, out DateTimeOffset fim)
    {
        inicio = default;
        fim = default;
        var dataIni = Texto(corpo, "dataini");
        var horaIni = Texto(corpo, "horaini");
        var dataFim = Texto(corpo, "datafim");
        var horaFim = Texto(corpo, "horafim");
        if (string.IsNullOrWhiteSpace(dataIni) || string.IsNullOrWhiteSpace(horaIni) || string.IsNullOrWhiteSpace(dataFim) || string.IsNullOrWhiteSpace(horaFim))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(dataIni + "T" + horaIni + ":00Z", out inicio))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(dataFim + "T" + horaFim + ":00Z", out fim))
        {
            return false;
        }

        return true;
    }

    private static string? Texto(JsonElement corpo, string nome)
    {
        if (!corpo.TryGetProperty(nome, out var valor) || valor.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return valor.GetString();
    }

    private static string TextoDoCodigo(string? codigo)
    {
        return codigo switch
        {
            "208" => "FALTAM PARÂMETROS",
            "215" => "ERRO: TAMANHO EXCEDIDO",
            "221" or "222" or "223" => "ERRO: LIMITE DE CONTATOS EXCEDIDO",
            _ => "ERRO: FORMATO INCORRETO",
        };
    }

    private static IReadOnlyList<ItemMailgrid> Falha(string codigo, string status)
    {
        return new[] { new ItemMailgrid(status, codigo, null, null) };
    }

    private static Dictionary<string, ItemMailgrid> Um(string codigo, string status)
    {
        return new Dictionary<string, ItemMailgrid> { ["0"] = new ItemMailgrid(status, codigo, null, null) };
    }
}

public sealed record AplicativoVisivel(
    string Id,
    string Nome,
    string HostSmtp,
    int PortaSmtp,
    string UsuarioSmtp,
    string SenhaSmtp,
    string TokenAuth,
    string CriadoEm);

public sealed record DestinoVisivel(string Id, string AplicativoId, string Canal, string Provedor, int Ordem, string Nome);
