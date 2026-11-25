using System.Net;
using System.Text;
using System.Text.Json;
using broker.Correio;
using Xunit;

namespace broker.Tests.Correio;

public sealed class ProvedorMailgridTests
{
    [Fact]
    public async Task Envia_pelo_sendmail_da_mailgrid_e_le_o_codigo_200()
    {
        string? url = null;
        string? corpoEnviado = null;
        var fabrica = new FabricaHttp(async (pedido, token) =>
        {
            url = pedido.RequestUri!.ToString();
            corpoEnviado = await pedido.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"status":"MSG ENVIADA","codigo":"200","criptokey":"mg-1","to":"pessoa@example.com"}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var provedor = new ProvedorMailgrid(fabrica);
        var resultado = await provedor.EnviarAsync(
            new PedidoAoProvedor(
                """{"host_smtp":"smtp.mailgrid.net.br","usuario_smtp":"u","senha_smtp":"s","url":"https://api.mailgrid.net.br"}""",
                "loja@example.com",
                "Loja",
                "pessoa@example.com",
                "Assunto",
                "<p>Oi</p>",
                "html",
                "entrega-1",
                Array.Empty<AnexoDeEnvio>()),
            CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal("mg-1", resultado.IdProvedor);
        Assert.Equal("https://api.mailgrid.net.br/sendmail/", url);
        using var corpo = JsonDocument.Parse(corpoEnviado!);
        Assert.Equal("smtp.mailgrid.net.br", corpo.RootElement.GetProperty("host_smtp").GetString());
        Assert.Equal("u", corpo.RootElement.GetProperty("usuario_smtp").GetString());
        Assert.Equal("s", corpo.RootElement.GetProperty("senha_smtp").GetString());
        Assert.Equal("pessoa@example.com", corpo.RootElement.GetProperty("emailDestino")[0].GetString());
    }

    [Fact]
    public async Task Segredo_incompleto_falha_sem_chamar_a_api()
    {
        var chamadas = 0;
        var fabrica = new FabricaHttp((_, _) =>
        {
            chamadas++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var provedor = new ProvedorMailgrid(fabrica);
        var resultado = await provedor.EnviarAsync(
            new PedidoAoProvedor(
                """{"usuario_smtp":"u"}""",
                "loja@example.com",
                "",
                "pessoa@example.com",
                "Assunto",
                "Oi",
                "text",
                "entrega-2",
                Array.Empty<AnexoDeEnvio>()),
            CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(0, chamadas);
    }

    private sealed class FabricaHttp : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _enviar;

        public FabricaHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> enviar)
        {
            _enviar = enviar;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new Manipulador(_enviar), disposeHandler: true);
        }

        private sealed class Manipulador : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _enviar;

            public Manipulador(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> enviar)
            {
                _enviar = enviar;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _enviar(request, cancellationToken);
            }
        }
    }
}
