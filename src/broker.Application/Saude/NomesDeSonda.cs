namespace broker.Health;

public static class NomesDeSonda
{
    public const string Banco = "banco";

    public const string Cache = "cache";

    public const string Workflows = "workflows";

    public const string Smtp = "smtp";

    public static IReadOnlyList<string> Todos { get; } = new[]
    {
        Banco,
        Cache,
        Workflows,
        Smtp,
    };
}
