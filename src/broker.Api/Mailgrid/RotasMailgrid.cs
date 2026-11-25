using broker.Caching;
using broker.Correio;
using System.Text.Json;

namespace broker.Api;

public sealed class RotasMailgrid : IModuloDeRotas
{
    public void Mapear(IEndpointRouteBuilder rotas)
    {
        Mapear(rotas, "/sendmail/");
        Mapear(rotas, "/send/");
        rotas.MapPost("/report/", (Delegate)RelatorioAsync);
        rotas.MapPost("/consolidate/", (Delegate)ConsolidarAsync);
        rotas.MapPost("/domain/add/", (Delegate)AdicionarAsync);
        rotas.MapPost("/domain/remove/", (Delegate)RemoverAsync);
        rotas.MapPost("/domain/check/", (Delegate)ChecarAsync);
        rotas.MapPost("/domain/list/", (Delegate)ListarAsync);
        rotas.MapPost("/api/mensagens", (Delegate)SmsAsync);
    }

    private static void Mapear(IEndpointRouteBuilder rotas, string caminho)
    {
        rotas.MapPost(caminho, EnviarAsync);
    }

    private static async Task<IResult> EnviarAsync(HttpRequest pedido, ServicoDeCorreio correio, ICacheDeLeitura cache, CancellationToken cancellationToken)
    {
        var corpo = await LerAsync(pedido, cancellationToken);
        if (corpo is null)
        {
            return Json(new[] { new ItemMailgrid("FALTAM PARÂMETROS", "208", null, null) });
        }

        var resultado = await correio.EnviarEmailAsync(corpo.Value, exigirHost: true, cancellationToken);
        await cache.RemoverPorEtiquetasAsync(new[] { "envios" }, cancellationToken);
        return Json(resultado);
    }

    private static async Task<IResult> SmsAsync(HttpRequest pedido, ServicoDeCorreio correio, ICacheDeLeitura cache, CancellationToken cancellationToken)
    {
        var corpo = await LerAsync(pedido, cancellationToken);
        if (corpo is null)
        {
            return Json(new ItemMailgrid("FALTAM PARÂMETROS", "208", null, null));
        }

        var resultado = await correio.EnviarSmsAsync(corpo.Value, cancellationToken);
        await cache.RemoverPorEtiquetasAsync(new[] { "envios", "mensagens" }, cancellationToken);
        return Json(resultado);
    }

    private static Task<IResult> RelatorioAsync(HttpRequest pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        return LerEAsync(pedido, correio.RelatorioAsync, cancellationToken);
    }

    private static Task<IResult> ConsolidarAsync(HttpRequest pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        return LerEAsync(pedido, correio.ConsolidarAsync, cancellationToken);
    }

    private static async Task<IResult> AdicionarAsync(HttpRequest pedido, ServicoDeCorreio correio, ICacheDeLeitura cache, CancellationToken cancellationToken)
    {
        var resultado = await ExecutarAsync(pedido, correio.AdicionarDominioAsync, cancellationToken);
        await cache.RemoverPorEtiquetasAsync(new[] { "dominios" }, cancellationToken);
        return resultado;
    }

    private static async Task<IResult> RemoverAsync(HttpRequest pedido, ServicoDeCorreio correio, ICacheDeLeitura cache, CancellationToken cancellationToken)
    {
        var resultado = await ExecutarAsync(pedido, correio.RemoverDominioAsync, cancellationToken);
        await cache.RemoverPorEtiquetasAsync(new[] { "dominios" }, cancellationToken);
        return resultado;
    }

    private static Task<IResult> ChecarAsync(HttpRequest pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        return ExecutarAsync(pedido, correio.ChecarDominioAsync, cancellationToken);
    }

    private static Task<IResult> ListarAsync(HttpRequest pedido, ServicoDeCorreio correio, CancellationToken cancellationToken)
    {
        return ExecutarAsync(pedido, correio.ListarDominiosAsync, cancellationToken);
    }

    private static Task<IResult> LerEAsync(HttpRequest pedido, Func<JsonElement, CancellationToken, Task<object>> acao, CancellationToken cancellationToken)
    {
        return ExecutarAsync(pedido, acao, cancellationToken);
    }

    private static async Task<IResult> ExecutarAsync(HttpRequest pedido, Func<JsonElement, CancellationToken, Task<object>> acao, CancellationToken cancellationToken)
    {
        var corpo = await LerAsync(pedido, cancellationToken);
        if (corpo is null)
        {
            return Json(new[] { new ItemMailgrid("FALTAM PARÂMETROS", "208", null, null) });
        }

        return Json(await acao(corpo.Value, cancellationToken));
    }

    private static async Task<JsonElement?> LerAsync(HttpRequest pedido, CancellationToken cancellationToken)
    {
        try
        {
            using var documento = await JsonDocument.ParseAsync(pedido.Body, cancellationToken: cancellationToken);
            return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Json(object corpo)
    {
        return Results.Json(corpo, statusCode: StatusCodes.Status200OK);
    }
}
