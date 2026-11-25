using System.Collections.Concurrent;
using System.Data.Common;
using Dapper;

namespace broker.Persistence.Adapters;

public sealed class MigradorSql
{
    private readonly IFabricaDeConexao _fabrica;
    private readonly ConcurrentDictionary<string, byte> _prontos = new();

    public MigradorSql(IFabricaDeConexao fabrica)
    {
        _fabrica = fabrica;
    }

    public async Task<DbConnection> AbrirAsync(IDialetoSql dialeto, CancellationToken cancellationToken)
    {
        var conexao = _fabrica.Criar();
        try
        {
            await conexao.OpenAsync(cancellationToken);
            var chave = conexao.ConnectionString ?? string.Empty;
            if (!_prontos.ContainsKey(chave))
            {
                await AplicarAsync(conexao, dialeto, cancellationToken);
                _prontos.TryAdd(chave, 0);
            }

            return conexao;
        }
        catch
        {
            await conexao.DisposeAsync();
            throw;
        }
    }

    private static async Task AplicarAsync(DbConnection conexao, IDialetoSql dialeto, CancellationToken cancellationToken)
    {
        await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirVersao, cancellationToken: cancellationToken));
        var aplicadas = new HashSet<int>();
        var versoes = await conexao.QueryAsync<int>(new CommandDefinition(dialeto.SqlLerVersao, cancellationToken: cancellationToken));
        foreach (var versao in versoes)
        {
            aplicadas.Add(versao);
        }

        if (!aplicadas.Contains(EsquemaDoBroker.Versao))
        {
            await conexao.ExecuteAsync(new CommandDefinition(dialeto.SqlGarantirBroker, cancellationToken: cancellationToken));
            await conexao.ExecuteAsync(new CommandDefinition(
                dialeto.SqlGravarVersao,
                new { versao = EsquemaDoBroker.Versao },
                cancellationToken: cancellationToken));
        }
    }
}
