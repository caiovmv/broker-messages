using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace broker.Correio;

public sealed class ProvedorSendGrid : IProvedorDeCanal, IRegistroDeDominio
{
    private readonly IHttpClientFactory _clientes;

    public ProvedorSendGrid(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    public string Nome => "sendgrid";

    public string Canal => "email";

    public async Task<ResultadoDeProvedor> EnviarAsync(PedidoAoProvedor pedido, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pedido.Segredo))
        {
            return new ResultadoDeProvedor(false, null);
        }

        var tipo = pedido.Tipo == "text" ? "text/plain" : "text/html";
        var corpo = new
        {
            personalizations = new[]
            {
                new
                {
                    to = new[] { new { email = pedido.Destinatario } },
                    custom_args = new Dictionary<string, string> { ["entrega"] = pedido.EntregaId },
                },
            },
            from = new { email = pedido.Remetente, name = pedido.NomeRemetente },
            subject = pedido.Assunto,
            content = new[] { new { type = tipo, value = pedido.Corpo } },
            attachments = pedido.Anexos.Select(item => new
            {
                content = item.ConteudoBase64,
                filename = item.Nome,
                type = item.Tipo,
            }).ToArray(),
        };
        var cliente = _clientes.CreateClient("provedores");
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pedido.Segredo.Trim());
        requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        using var resposta = await cliente.SendAsync(requisicao, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            return new ResultadoDeProvedor(false, null);
        }

        var id = resposta.Headers.TryGetValues("X-Message-Id", out var valores) ? valores.FirstOrDefault() : null;
        return new ResultadoDeProvedor(true, string.IsNullOrWhiteSpace(id) ? pedido.EntregaId : id);
    }

    public async Task RegistrarAsync(string segredo, DominioRemetente dominio, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(segredo))
        {
            return;
        }

        try
        {
            var cliente = _clientes.CreateClient("provedores");
            using var requisicao = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/whitelabel/domains");
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", segredo.Trim());
            requisicao.Content = new StringContent(
                JsonSerializer.Serialize(new { domain = dominio.Nome, automatic_security = true }),
                Encoding.UTF8,
                "application/json");
            using var resposta = await cliente.SendAsync(requisicao, cancellationToken);
            _ = resposta.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }
}

public sealed class ProvedorMailgrid : IProvedorDeCanal
{
    private readonly IHttpClientFactory _clientes;

    public ProvedorMailgrid(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    public string Nome => "mailgrid";

    public string Canal => "email";

    public async Task<ResultadoDeProvedor> EnviarAsync(PedidoAoProvedor pedido, CancellationToken cancellationToken)
    {
        SegredoMailgrid? segredo;
        try
        {
            segredo = JsonSerializer.Deserialize<SegredoMailgrid>(pedido.Segredo);
        }
        catch (JsonException)
        {
            return new ResultadoDeProvedor(false, null);
        }

        if (segredo is null
            || string.IsNullOrWhiteSpace(segredo.host_smtp)
            || string.IsNullOrWhiteSpace(segredo.usuario_smtp)
            || string.IsNullOrWhiteSpace(segredo.senha_smtp))
        {
            return new ResultadoDeProvedor(false, null);
        }

        var baseUrl = string.IsNullOrWhiteSpace(segredo.url) ? "https://api.mailgrid.net.br" : segredo.url.TrimEnd('/');
        var corpo = new Dictionary<string, object?>
        {
            ["host_smtp"] = segredo.host_smtp.Trim(),
            ["usuario_smtp"] = segredo.usuario_smtp.Trim(),
            ["senha_smtp"] = segredo.senha_smtp,
            ["emailRemetente"] = pedido.Remetente,
            ["nomeRemetente"] = pedido.NomeRemetente,
            ["emailDestino"] = new[] { pedido.Destinatario },
            ["assunto"] = pedido.Assunto,
            ["mensagem"] = pedido.Corpo,
            ["mensagemTipo"] = pedido.Tipo == "text" ? "text" : "html",
        };
        if (pedido.Anexos.Count > 0)
        {
            corpo["mensagemAnexos"] = pedido.Anexos.Select(item => new
            {
                name = item.Nome,
                type = item.Tipo,
                content = item.ConteudoBase64,
            }).ToArray();
        }

        var cliente = _clientes.CreateClient("provedores");
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/sendmail/");
        requisicao.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        using var resposta = await cliente.SendAsync(requisicao, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            return new ResultadoDeProvedor(false, null);
        }

        var texto = await resposta.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var documento = JsonDocument.Parse(texto);
            if (documento.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in documento.RootElement.EnumerateArray())
                {
                    var codigo = item.TryGetProperty("codigo", out var valor) ? valor.GetString() : null;
                    if (codigo == "200")
                    {
                        var id = item.TryGetProperty("criptokey", out var chave) ? chave.GetString() : null;
                        return new ResultadoDeProvedor(true, string.IsNullOrWhiteSpace(id) ? pedido.EntregaId : id);
                    }
                }
            }
        }
        catch (JsonException)
        {
            return new ResultadoDeProvedor(false, null);
        }

        return new ResultadoDeProvedor(false, null);
    }

    private sealed class SegredoMailgrid
    {
        public string url { get; set; } = "";

        public string host_smtp { get; set; } = "";

        public string usuario_smtp { get; set; } = "";

        public string senha_smtp { get; set; } = "";
    }
}

public sealed class ProvedorTwilio : IProvedorDeCanal
{
    private readonly IHttpClientFactory _clientes;

    public ProvedorTwilio(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    public string Nome => "twilio";

    public string Canal => "sms";

    public async Task<ResultadoDeProvedor> EnviarAsync(PedidoAoProvedor pedido, CancellationToken cancellationToken)
    {
        SegredoTwilio? segredo;
        try
        {
            segredo = JsonSerializer.Deserialize<SegredoTwilio>(pedido.Segredo);
        }
        catch (JsonException)
        {
            return new ResultadoDeProvedor(false, null);
        }

        if (segredo is null || string.IsNullOrWhiteSpace(segredo.sid) || string.IsNullOrWhiteSpace(segredo.token) || string.IsNullOrWhiteSpace(segredo.from))
        {
            return new ResultadoDeProvedor(false, null);
        }

        var cliente = _clientes.CreateClient("provedores");
        using var requisicao = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.twilio.com/2010-04-01/Accounts/" + segredo.sid + "/Messages.json");
        var basico = Convert.ToBase64String(Encoding.UTF8.GetBytes(segredo.sid + ":" + segredo.token));
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Basic", basico);
        requisicao.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = pedido.Destinatario,
            ["From"] = segredo.from,
            ["Body"] = pedido.Corpo,
        });
        using var resposta = await cliente.SendAsync(requisicao, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
        {
            return new ResultadoDeProvedor(false, null);
        }

        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(cancellationToken));
        var sid = documento.RootElement.TryGetProperty("sid", out var valor) ? valor.GetString() : null;
        return new ResultadoDeProvedor(true, sid);
    }

    private sealed class SegredoTwilio
    {
        public string sid { get; set; } = "";

        public string token { get; set; } = "";

        public string from { get; set; } = "";
    }
}

public sealed class ConsultaDns : IConsultaDeDns
{
    private readonly IHttpClientFactory _clientes;

    public ConsultaDns(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    public async Task<bool> ContemTxtAsync(string nome, string trecho, CancellationToken cancellationToken)
    {
        try
        {
            var cliente = _clientes.CreateClient("provedores");
            using var requisicao = new HttpRequestMessage(
                HttpMethod.Get,
                "https://cloudflare-dns.com/dns-query?name=" + Uri.EscapeDataString(nome) + "&type=TXT");
            requisicao.Headers.Accept.ParseAdd("application/dns-json");
            using var resposta = await cliente.SendAsync(requisicao, cancellationToken);
            if (!resposta.IsSuccessStatusCode)
            {
                return false;
            }

            var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
            return corpo.Contains(trecho, StringComparison.Ordinal);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
