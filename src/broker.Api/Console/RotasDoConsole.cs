using broker.Correio;

namespace broker.Api;

public sealed class RotasDoConsole : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/api/sessao", EntrarAsync);
        rotas.MapDelete("/api/sessao", Sair);
        rotas.MapGet("/api/sessao", Eu);
        rotas.MapGet("/api/aplicativos", (ServicoDeCorreio correio, CancellationToken token) => correio.AplicativosAsync(token));
        rotas.MapPost("/api/aplicativos", CriarAplicativoAsync);
        rotas.MapPost("/api/aplicativos/{id}/rotacao", (string id, ServicoDeCorreio correio, CancellationToken token) => correio.RotacionarAsync(id, token));
        rotas.MapGet("/api/destinos", (string aplicativo, string? canal, ServicoDeCorreio correio, CancellationToken token) => correio.DestinosVisiveisAsync(aplicativo, canal, token));
        rotas.MapPost("/api/destinos", CriarDestinoAsync);
        rotas.MapPut("/api/destinos/ordem", OrdenarAsync);
        rotas.MapDelete("/api/destinos/{id}", RemoverDestinoAsync);
        rotas.MapGet("/api/dominios", (string aplicativo, ServicoDeCorreio correio, CancellationToken token) => correio.DominiosDoConsoleAsync(aplicativo, token));
        rotas.MapGet("/api/envios", (string aplicativo, ServicoDeCorreio correio, CancellationToken token) => correio.EnviosAsync(aplicativo, token));
    }

    private static IResult EntrarAsync(PedidoDeSessao pedido, SessoesDoOperador sessoes, HttpContext contexto)
    {
        var token = sessoes.Abrir(pedido.Usuario, pedido.Senha);
        if (token is null)
        {
            return Results.Json(new { detail = "credentials were rejected" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        contexto.Response.Cookies.Append(SessoesDoOperador.Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
        return Results.Ok(new { nome = pedido.Usuario });
    }

    private static IResult Sair(HttpContext contexto, SessoesDoOperador sessoes)
    {
        if (contexto.Request.Cookies.TryGetValue(SessoesDoOperador.Cookie, out var token))
        {
            sessoes.Fechar(token);
        }

        contexto.Response.Cookies.Delete(SessoesDoOperador.Cookie);
        return Results.Ok(new { ok = true });
    }

    private static IResult Eu(HttpContext contexto)
    {
        var nome = contexto.Items["operador"] as string;
        if (string.IsNullOrWhiteSpace(nome))
        {
            return Results.Json(new { detail = "sign in is required" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(new { nome });
    }

    private static Task<AplicativoVisivel> CriarAplicativoAsync(PedidoDeAplicativo pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        return correio.CriarAplicativoAsync(pedido.Nome, cancellationToken);
    }

    private static Task<DestinoVisivel> CriarDestinoAsync(PedidoDeDestino pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        return correio.CriarDestinoAsync(pedido.AplicativoId, pedido.Canal, pedido.Provedor, pedido.Nome, pedido.Segredo, cancellationToken);
    }

    private static async Task<IResult> OrdenarAsync(PedidoDeOrdem pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        await correio.ReordenarAsync(pedido.AplicativoId, pedido.Canal, pedido.Ids, cancellationToken);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> RemoverDestinoAsync(string id, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        await correio.RemoverDestinoAsync(id, cancellationToken);
        return Results.Ok(new { ok = true });
    }
}

public sealed class RotasDeWebhook : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/webhooks/sendgrid", SendGridAsync);
        rotas.MapPost("/webhooks/twilio", TwilioAsync);
    }

    private static async Task<IResult> SendGridAsync(HttpRequest pedido, ServicoDeCorreio correio, IConfiguration configuracao, broker.Caching.ICacheDeLeitura cache, CancellationToken cancellationToken)
    {
        if (!Autorizado(pedido, configuracao))
        {
            return Results.Unauthorized();
        }

        using var documento = await System.Text.Json.JsonDocument.ParseAsync(pedido.Body, cancellationToken: cancellationToken);
        if (documento.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var evento in documento.RootElement.EnumerateArray())
            {
                var entrega = evento.TryGetProperty("entrega", out var id) ? id.GetString() : null;
                var provedor = evento.TryGetProperty("sg_message_id", out var mensagem) ? mensagem.GetString() : null;
                var nome = evento.TryGetProperty("event", out var tipo) ? tipo.GetString() ?? "" : "";
                var classe = evento.TryGetProperty("type", out var detalhe) ? detalhe.GetString() : null;
                await correio.AtualizarPorProvedorAsync(entrega, provedor, nome + " " + classe, cancellationToken);
            }
        }

        await cache.RemoverPorEtiquetasAsync(new[] { "envios" }, cancellationToken);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> TwilioAsync(HttpRequest pedido, ServicoDeCorreio correio, IConfiguration configuracao, broker.Caching.ICacheDeLeitura cache, CancellationToken cancellationToken)
    {
        if (!Autorizado(pedido, configuracao))
        {
            return Results.Unauthorized();
        }

        var form = await pedido.ReadFormAsync(cancellationToken);
        await correio.AtualizarPorProvedorAsync(null, form["MessageSid"], form["MessageStatus"].ToString(), cancellationToken);
        await cache.RemoverPorEtiquetasAsync(new[] { "envios" }, cancellationToken);
        return Results.Ok(new { ok = true });
    }

    private static bool Autorizado(HttpRequest pedido, IConfiguration configuracao)
    {
        var esperado = configuracao["Broker:WebhookSegredo"] ?? "";
        if (esperado.Length == 0)
        {
            return false;
        }

        var recebido = pedido.Headers["X-Broker-Webhook"].ToString();
        if (recebido.Length == 0)
        {
            recebido = pedido.Query["segredo"].ToString();
        }

        return broker.Correio.ResumoDeSegredo.Iguais(esperado, recebido);
    }
}

public sealed record PedidoDeSessao(string Usuario, string Senha);

public sealed record PedidoDeAplicativo(string Nome);

public sealed record PedidoDeDestino(string AplicativoId, string Canal, string Provedor, string Nome, string Segredo);

public sealed record PedidoDeOrdem(string AplicativoId, string Canal, IReadOnlyList<string> Ids);
