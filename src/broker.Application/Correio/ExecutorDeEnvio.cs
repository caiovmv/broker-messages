using System.Text.Json;

namespace broker.Correio;

public sealed class ExecutorDeEnvio : IExecutorDeEnvio
{
    private readonly IAcervo _acervo;
    private readonly IProtecaoDeSegredo _protecao;
    private readonly IEnumerable<IProvedorDeCanal> _provedores;
    private readonly IConsultaDeDns _dns;
    private readonly IEnumerable<IRegistroDeDominio> _registros;

    public ExecutorDeEnvio(
        IAcervo acervo,
        IProtecaoDeSegredo protecao,
        IEnumerable<IProvedorDeCanal> provedores,
        IConsultaDeDns dns,
        IEnumerable<IRegistroDeDominio> registros)
    {
        _acervo = acervo;
        _protecao = protecao;
        _provedores = provedores;
        _dns = dns;
        _registros = registros;
    }

    public async Task EnviarAsync(string mensagemId, CancellationToken cancellationToken)
    {
        var mensagem = await _acervo.MensagemAsync(mensagemId, cancellationToken);
        if (mensagem is null)
        {
            return;
        }

        var entregas = await _acervo.EntregasDaMensagemAsync(mensagemId, cancellationToken);
        var destinos = await _acervo.DestinosAsync(mensagem.AplicativoId, mensagem.Canal, cancellationToken);
        var anexos = LerAnexos(mensagem.Anexos);
        foreach (var entrega in entregas)
        {
            if (entrega.Codigo == "200" && entrega.IdProvedor.Length > 0)
            {
                continue;
            }

            var tentativas = await _acervo.TentativasAsync(entrega.Id, cancellationToken);
            var enviou = false;
            foreach (var destino in destinos.OrderBy(item => item.Ordem))
            {
                if (tentativas.Any(item => item.DestinoId == destino.Id))
                {
                    continue;
                }

                var abriu = await _acervo.AbrirTentativaAsync(entrega.Id, destino.Id, cancellationToken);
                if (!abriu)
                {
                    continue;
                }

                var provedor = _provedores.FirstOrDefault(item =>
                    item.Nome.Equals(destino.Provedor, StringComparison.OrdinalIgnoreCase)
                    && item.Canal.Equals(destino.Canal, StringComparison.OrdinalIgnoreCase));
                ResultadoDeProvedor resultado;
                if (provedor is null)
                {
                    resultado = new ResultadoDeProvedor(false, null);
                }
                else
                {
                    var segredo = destino.SegredoProtegido.Length == 0
                        ? ""
                        : _protecao.Revelar(destino.SegredoProtegido);
                    resultado = await provedor.EnviarAsync(
                        new PedidoAoProvedor(
                            segredo,
                            mensagem.Remetente,
                            mensagem.NomeRemetente,
                            entrega.Destinatario,
                            entrega.AssuntoPersonalizado.Length == 0 ? mensagem.Assunto : entrega.AssuntoPersonalizado,
                            entrega.CorpoPersonalizado.Length == 0 ? mensagem.Corpo : entrega.CorpoPersonalizado,
                            "html",
                            entrega.Id,
                            anexos),
                        cancellationToken);
                }

                await _acervo.FecharTentativaAsync(entrega.Id, destino.Id, resultado.Sucesso, resultado.IdProvedor, cancellationToken);
                if (resultado.Sucesso)
                {
                    await _acervo.AtualizarEntregaAsync(
                        entrega.Id,
                        "200",
                        "Entregue com sucesso",
                        destino.Provedor,
                        resultado.IdProvedor,
                        cancellationToken);
                    enviou = true;
                    break;
                }
            }

            if (!enviou && entrega.Codigo != "200")
            {
                await _acervo.AtualizarEntregaAsync(entrega.Id, "204", "ERRO DE ENVIO", null, null, cancellationToken);
            }
        }
    }

    public async Task VerificarDominioAsync(string dominioId, CancellationToken cancellationToken)
    {
        var dominio = await _acervo.DominioPorIdAsync(dominioId, cancellationToken);
        if (dominio is null)
        {
            return;
        }

        var destinos = await _acervo.DestinosAsync(dominio.AplicativoId, "email", cancellationToken);
        foreach (var destino in destinos.Where(item => item.Provedor.Equals("sendgrid", StringComparison.OrdinalIgnoreCase)))
        {
            var segredo = destino.SegredoProtegido.Length == 0 ? "" : _protecao.Revelar(destino.SegredoProtegido);
            foreach (var registro in _registros)
            {
                await registro.RegistrarAsync(segredo, dominio, cancellationToken);
            }
        }

        var ok = await _dns.ContemTxtAsync(dominio.ChaveNome, dominio.ChaveValor, cancellationToken);
        dominio.Validado = ok ? "validado" : "não validado";
        dominio.Liberado = ok ? "liberado para envios" : "não liberado";
        await _acervo.AtualizarDominioAsync(dominio, cancellationToken);
    }

    private static IReadOnlyList<AnexoDeEnvio> LerAnexos(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<AnexoDeEnvio>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<AnexoDeEnvio>>(json) ?? new List<AnexoDeEnvio>();
        }
        catch (JsonException)
        {
            return Array.Empty<AnexoDeEnvio>();
        }
    }
}
