using System.Text.RegularExpressions;

namespace broker.Api;

public static partial class RedacaoDeSegredo
{
    public static string Aplicar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        return Segredo().Replace(texto, "$1***");
    }

    [GeneratedRegex("(senha|password|token|api_key|apikey|secret)=([^&\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Segredo();
}
