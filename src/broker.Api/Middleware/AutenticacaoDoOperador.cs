using System.Collections.Concurrent;
using broker.Correio;

namespace broker.Api;

public sealed class SessoesDoOperador
{
    public const string Cookie = "broker_session";

    private readonly ConcurrentDictionary<string, string> _abertas = new();
    private readonly string _usuario;
    private readonly string _senha;

    public SessoesDoOperador(string usuario, string senha)
    {
        _usuario = usuario;
        _senha = senha;
    }

    public string? Abrir(string? usuario, string? senha)
    {
        if (string.IsNullOrEmpty(_usuario) || string.IsNullOrEmpty(_senha))
        {
            return null;
        }

        if (!ResumoDeSegredo.Iguais(_usuario, usuario ?? "") || !ResumoDeSegredo.Iguais(_senha, senha ?? ""))
        {
            return null;
        }

        var token = Guid.NewGuid().ToString("N");
        _abertas[token] = _usuario;
        return token;
    }

    public string? Nome(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return _abertas.TryGetValue(token, out var nome) ? nome : null;
    }

    public void Fechar(string token)
    {
        _abertas.TryRemove(token, out _);
    }
}

public sealed class AutenticacaoDoOperador
{
    private readonly RequestDelegate _proximo;

    public AutenticacaoDoOperador(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto, SessoesDoOperador sessoes)
    {
        contexto.Request.Cookies.TryGetValue(SessoesDoOperador.Cookie, out var token);
        var nome = sessoes.Nome(token);
        if (!string.IsNullOrWhiteSpace(nome))
        {
            contexto.Items["operador"] = nome;
        }

        if (Exige(contexto.Request.Path) && string.IsNullOrWhiteSpace(nome))
        {
            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await contexto.Response.WriteAsJsonAsync(new { detail = "sign in is required" });
            return;
        }

        await _proximo(contexto);
    }

    private static bool Exige(PathString caminho)
    {
        if (!caminho.StartsWithSegments("/api"))
        {
            return false;
        }

        if (caminho.StartsWithSegments("/api/sessao") || caminho.StartsWithSegments("/api/mensagens"))
        {
            return false;
        }

        return true;
    }
}

public static class AutenticacaoDoOperadorExtensions
{
    public static WebApplication UseAutenticacaoDoOperador(this WebApplication app)
    {
        app.UseMiddleware<AutenticacaoDoOperador>();
        return app;
    }
}
