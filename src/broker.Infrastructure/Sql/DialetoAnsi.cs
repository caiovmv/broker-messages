namespace broker.Persistence.Adapters;

public sealed class DialetoAnsi : IDialetoSql
{
    public static readonly DialetoAnsi Instancia = new();

    private DialetoAnsi()
    {
    }

    public string SqlGarantirVersao => SqlDeEsquema.GarantirVersaoAnsi;

    public string SqlLerVersao => SqlDeEsquema.LerVersao;

    public string SqlGravarVersao => SqlDeEsquema.GravarVersao;

    public string SqlGarantirBroker => EsquemaDoBroker.Ansi;

    public string Paginacao => "OFFSET @deslocamento ROWS FETCH NEXT @tamanho ROWS ONLY";
}
