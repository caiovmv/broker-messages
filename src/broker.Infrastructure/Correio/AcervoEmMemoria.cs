using System.Globalization;

namespace broker.Correio;

public sealed class AcervoEmMemoria : IAcervo
{
    private readonly object _porta = new();
    private readonly List<Aplicativo> _aplicativos = new();
    private readonly List<Destino> _destinos = new();
    private readonly List<DominioRemetente> _dominios = new();
    private readonly List<MensagemGravada> _mensagens = new();
    private readonly List<EntregaGravada> _entregas = new();
    private readonly List<TentativaGravada> _tentativas = new();

    public Task<Aplicativo?> AplicativoPorIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_aplicativos.FirstOrDefault(item => item.Id == id));
        }
    }

    public Task<Aplicativo?> AplicativoPorUsuarioAsync(string usuario, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_aplicativos.FirstOrDefault(item => item.UsuarioSmtp.Equals(usuario, StringComparison.Ordinal)));
        }
    }

    public Task<Aplicativo?> AplicativoPorTokenAsync(string tokenBusca, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_aplicativos.FirstOrDefault(item => item.TokenBusca == tokenBusca));
        }
    }

    public Task<IReadOnlyList<Aplicativo>> AplicativosAsync(CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult<IReadOnlyList<Aplicativo>>(_aplicativos.ToArray());
        }
    }

    public Task InserirAplicativoAsync(Aplicativo aplicativo, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            _aplicativos.Add(aplicativo);
        }

        return Task.CompletedTask;
    }

    public Task AtualizarSegredosAsync(string id, string senhaProtegida, string tokenBusca, string tokenProtegido, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            var aplicativo = _aplicativos.First(item => item.Id == id);
            aplicativo.SenhaProtegida = senhaProtegida;
            aplicativo.TokenBusca = tokenBusca;
            aplicativo.TokenProtegido = tokenProtegido;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Destino>> DestinosAsync(string aplicativoId, string? canal, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult<IReadOnlyList<Destino>>(_destinos
                .Where(item => item.AplicativoId == aplicativoId && (canal is null || item.Canal == canal))
                .OrderBy(item => item.Ordem)
                .ToArray());
        }
    }

    public Task InserirDestinoAsync(Destino destino, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            _destinos.Add(destino);
        }

        return Task.CompletedTask;
    }

    public Task ReordenarDestinosAsync(string aplicativoId, string canal, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var destino = _destinos.FirstOrDefault(item => item.Id == ids[i] && item.AplicativoId == aplicativoId && item.Canal == canal);
                if (destino is not null)
                {
                    destino.Ordem = i + 1;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoverDestinoAsync(string id, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            _destinos.RemoveAll(item => item.Id == id);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DominioRemetente>> DominiosAsync(string aplicativoId, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult<IReadOnlyList<DominioRemetente>>(_dominios.Where(item => item.AplicativoId == aplicativoId).ToArray());
        }
    }

    public Task<DominioRemetente?> DominioAsync(string aplicativoId, string nome, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_dominios.FirstOrDefault(item => item.AplicativoId == aplicativoId && item.Nome == nome));
        }
    }

    public Task<DominioRemetente?> DominioPorIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_dominios.FirstOrDefault(item => item.Id == id));
        }
    }

    public Task InserirDominioAsync(DominioRemetente dominio, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            _dominios.Add(dominio);
        }

        return Task.CompletedTask;
    }

    public Task AtualizarDominioAsync(DominioRemetente dominio, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            var atual = _dominios.First(item => item.Id == dominio.Id);
            atual.Validado = dominio.Validado;
            atual.Liberado = dominio.Liberado;
            atual.SpfNome = dominio.SpfNome;
            atual.SpfValor = dominio.SpfValor;
            atual.DkimNome = dominio.DkimNome;
            atual.DkimValor = dominio.DkimValor;
            atual.ChaveNome = dominio.ChaveNome;
            atual.ChaveValor = dominio.ChaveValor;
        }

        return Task.CompletedTask;
    }

    public Task RemoverDominioAsync(string id, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            _dominios.RemoveAll(item => item.Id == id);
        }

        return Task.CompletedTask;
    }

    public Task InserirMensagemAsync(MensagemGravada mensagem, IReadOnlyList<EntregaGravada> entregas, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            _mensagens.Add(mensagem);
            _entregas.AddRange(entregas);
        }

        return Task.CompletedTask;
    }

    public Task<MensagemGravada?> MensagemAsync(string id, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_mensagens.FirstOrDefault(item => item.Id == id));
        }
    }

    public Task<IReadOnlyList<EntregaGravada>> EntregasDaMensagemAsync(string mensagemId, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult<IReadOnlyList<EntregaGravada>>(_entregas.Where(item => item.MensagemId == mensagemId).ToArray());
        }
    }

    public Task<IReadOnlyList<EntregaConsulta>> ConsultarAsync(string aplicativoId, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            var linhas = from entrega in _entregas
                         join mensagem in _mensagens on entrega.MensagemId equals mensagem.Id
                         where mensagem.AplicativoId == aplicativoId
                         select new EntregaConsulta(
                             mensagem.Id,
                             mensagem.MsgId,
                             mensagem.Remetente,
                             entrega.Destinatario,
                             entrega.Status,
                             entrega.Codigo,
                             entrega.AtualizadoEm,
                             mensagem.Canal);
            return Task.FromResult<IReadOnlyList<EntregaConsulta>>(linhas.ToArray());
        }
    }

    public Task<EntregaGravada?> EntregaPorIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_entregas.FirstOrDefault(item => item.Id == id));
        }
    }

    public Task<EntregaGravada?> EntregaPorProvedorAsync(string idProvedor, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult(_entregas.FirstOrDefault(item =>
                item.IdProvedor.Length > 0 && (item.IdProvedor == idProvedor || idProvedor.StartsWith(item.IdProvedor, StringComparison.Ordinal))));
        }
    }

    public Task AtualizarEntregaAsync(string id, string codigo, string status, string? provedor, string? idProvedor, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            var entrega = _entregas.First(item => item.Id == id);
            entrega.Codigo = codigo;
            entrega.Status = status;
            if (provedor is not null)
            {
                entrega.Provedor = provedor;
            }

            if (idProvedor is not null)
            {
                entrega.IdProvedor = idProvedor;
            }

            entrega.AtualizadoEm = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TentativaGravada>> TentativasAsync(string entregaId, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            return Task.FromResult<IReadOnlyList<TentativaGravada>>(_tentativas.Where(item => item.EntregaId == entregaId).ToArray());
        }
    }

    public Task<bool> AbrirTentativaAsync(string entregaId, string destinoId, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            if (_tentativas.Any(item => item.EntregaId == entregaId && item.DestinoId == destinoId))
            {
                return Task.FromResult(false);
            }

            _tentativas.Add(new TentativaGravada { EntregaId = entregaId, DestinoId = destinoId, Aberta = true });
            return Task.FromResult(true);
        }
    }

    public Task FecharTentativaAsync(string entregaId, string destinoId, bool sucesso, string? idProvedor, CancellationToken cancellationToken)
    {
        lock (_porta)
        {
            var tentativa = _tentativas.First(item => item.EntregaId == entregaId && item.DestinoId == destinoId);
            tentativa.Aberta = false;
            tentativa.Sucesso = sucesso;
            tentativa.IdProvedor = idProvedor ?? "";
        }

        return Task.CompletedTask;
    }
}
