namespace broker.Persistence.Adapters;

internal static class SqlDeEsquema
{
    public const string GarantirVersaoAnsi =
        """
        CREATE TABLE IF NOT EXISTS esquema_versao (
            versao integer NOT NULL PRIMARY KEY
        )
        """;

    public const string GarantirVersaoSqlServer =
        """
        IF OBJECT_ID(N'esquema_versao', N'U') IS NULL
        BEGIN
            CREATE TABLE esquema_versao (
                versao int NOT NULL PRIMARY KEY
            );
        END
        """;

    public const string LerVersao = "SELECT versao FROM esquema_versao";

    public const string GravarVersao = "INSERT INTO esquema_versao (versao) VALUES (@versao)";
}
