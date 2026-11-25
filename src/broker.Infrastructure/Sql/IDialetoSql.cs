namespace broker.Persistence.Adapters;

public interface IDialetoSql
{
    string SqlGarantirVersao { get; }

    string SqlLerVersao { get; }

    string SqlGravarVersao { get; }

    string SqlGarantirBroker { get; }

    string Paginacao { get; }
}
