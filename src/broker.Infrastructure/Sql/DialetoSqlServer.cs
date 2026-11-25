namespace broker.Persistence.Adapters;

public sealed class DialetoSqlServer : IDialetoSql
{
    public static readonly DialetoSqlServer Instancia = new();

    private DialetoSqlServer()
    {
    }

    public string SqlGarantirVersao => SqlDeEsquema.GarantirVersaoSqlServer;

    public string SqlLerVersao => SqlDeEsquema.LerVersao;

    public string SqlGravarVersao => SqlDeEsquema.GravarVersao;

    public string SqlGarantirBroker => EsquemaDoBroker.SqlServer;

    public string Paginacao => "OFFSET @deslocamento ROWS FETCH NEXT @tamanho ROWS ONLY";
}
