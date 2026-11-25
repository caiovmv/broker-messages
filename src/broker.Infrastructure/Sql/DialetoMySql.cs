namespace broker.Persistence.Adapters;

public sealed class DialetoMySql : IDialetoSql
{
    public static readonly DialetoMySql Instancia = new();

    private DialetoMySql()
    {
    }

    public string SqlGarantirVersao => SqlDeEsquema.GarantirVersaoAnsi;

    public string SqlLerVersao => SqlDeEsquema.LerVersao;

    public string SqlGravarVersao => SqlDeEsquema.GravarVersao;

    public string SqlGarantirBroker => EsquemaDoBroker.Ansi;

    public string Paginacao => "LIMIT @tamanho OFFSET @deslocamento";
}
