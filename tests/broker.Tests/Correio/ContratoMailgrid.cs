using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using broker.Api;
using broker.Correio;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace broker.Tests.Correio;

public sealed class ProvedorContado : IProvedorDeCanal
{
    public ProvedorContado(string nome, string canal)
    {
        Nome = nome;
        Canal = canal;
    }

    public string Nome { get; }

    public string Canal { get; }

    public int Chamadas { get; private set; }

    public Task<ResultadoDeProvedor> EnviarAsync(PedidoAoProvedor pedido, CancellationToken cancellationToken)
    {
        Chamadas++;
        var falha = pedido.Segredo.Contains("\"falha\":true", StringComparison.Ordinal);
        return Task.FromResult(falha
            ? new ResultadoDeProvedor(false, null)
            : new ResultadoDeProvedor(true, "prov-" + pedido.EntregaId));
    }
}

[CollectionDefinition("broker")]
public sealed class ColecaoDoBroker;

public class FabricaDoBroker : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _extras;

    public FabricaDoBroker(Dictionary<string, string?>? extras = null)
    {
        _extras = extras ?? new Dictionary<string, string?>();
        Definir("Broker__OperadorUsuario", "operador");
        Definir("Broker__OperadorSenha", "segredo-teste");
        Definir("Broker__HostSmtp", "smtp.broker.local");
        Definir("Broker__PortaSmtp", "587");
        Definir("Broker__WebhookSegredo", "gancho");
        Definir("Smtp__Escutar", "true");
        Definir("Smtp__Porta", "0");
        Definir("Smtp__Certificado", "");
        Definir("Smtp__SenhaDoCertificado", "");
        Definir("Database__ConnectionString", "");
        Definir("Database__Provider", "Sqlite");
        Definir("Workflows__Endpoint", "");
        foreach (var par in _extras)
        {
            Definir(par.Key, par.Value);
        }
    }

    private static void Definir(string chave, string? valor)
    {
        Environment.SetEnvironmentVariable(chave, valor);
    }

    public ProvedorContado Email { get; } = new("falso", "email");

    public ProvedorContado Sms { get; } = new("falso", "sms");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuracao) =>
        {
            var memoria = new Dictionary<string, string?>
            {
                ["Broker:OperadorUsuario"] = "operador",
                ["Broker:OperadorSenha"] = "segredo-teste",
                ["Broker:HostSmtp"] = "smtp.broker.local",
                ["Broker:PortaSmtp"] = "587",
                ["Broker:WebhookSegredo"] = "gancho",
                ["Smtp:Escutar"] = "true",
                ["Smtp:Porta"] = "0",
                ["Smtp:Certificado"] = "",
                ["Smtp:SenhaDoCertificado"] = "",
                ["Database:ConnectionString"] = "",
                ["Database:Provider"] = "Sqlite",
                ["Workflows:Endpoint"] = "",
            };
            foreach (var par in _extras)
            {
                memoria[par.Key.Replace("__", ":", StringComparison.Ordinal)] = par.Value;
            }

            configuracao.AddInMemoryCollection(memoria);
        });
        builder.ConfigureServices(servicos =>
        {
            servicos.AddSingleton<IProvedorDeCanal>(Email);
            servicos.AddSingleton<IProvedorDeCanal>(Sms);
        });
    }
}

[Collection("broker")]
public sealed class ContratoMailgrid : IDisposable
{
    private readonly FabricaDoBroker _fabrica = new();

    public void Dispose()
    {
        _fabrica.Dispose();
    }

    [Fact]
    public async Task Envio_relatorio_dominio_fallback_e_cache()
    {
        using var cliente = _fabrica.CreateClient();
        var entrada = await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" });
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);

        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome = "Loja" });
        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        Assert.Contains("aplicativos", criado.Headers.GetValues("X-Cache-Evict").Single());
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var raiz = aplicativo.RootElement;
        var id = raiz.GetProperty("id").GetString()!;
        var usuario = raiz.GetProperty("usuarioSmtp").GetString()!;
        var senha = raiz.GetProperty("senhaSmtp").GetString()!;
        var token = raiz.GetProperty("tokenAuth").GetString()!;
        Assert.Equal("smtp.broker.local", raiz.GetProperty("hostSmtp").GetString());
        Assert.False(string.IsNullOrWhiteSpace(senha));
        Assert.False(string.IsNullOrWhiteSpace(token));

        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId = id,
            canal = "email",
            provedor = "falso",
            nome = "primario",
            segredo = "{\"falha\":true}",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId = id,
            canal = "email",
            provedor = "falso",
            nome = "reserva",
            segredo = "{\"falha\":false}",
        })).StatusCode);

        var antes = _fabrica.Email.Chamadas;
        var enviadoResposta = await cliente.PostAsync("/sendmail/", Json(new
        {
            host_smtp = "smtp.broker.local",
            usuario_smtp = usuario,
            senha_smtp = senha,
            emailRemetente = "loja@smtp.broker.local",
            emailDestino = new[] { "pessoa@example.com" },
            assunto = "Ola {{nome}}",
            mensagem = "<p>Ola</p>",
            variaveis = new[] { new { nome = "Ana" } },
        }));
        Assert.Equal(HttpStatusCode.OK, enviadoResposta.StatusCode);
        using var enviado = JsonDocument.Parse(await enviadoResposta.Content.ReadAsStringAsync());
        var itens = enviado.RootElement.EnumerateArray().ToArray();
        Assert.Equal("200", itens[0].GetProperty("codigo").GetString());
        Assert.Equal("MSG ENVIADA", itens[0].GetProperty("status").GetString());
        Assert.Equal("pessoa@example.com", itens[0].GetProperty("to").GetString());
        Assert.Equal(antes + 2, _fabrica.Email.Chamadas);

        using var faltou = await PostarAsync(cliente, "/send/", new
        {
            host_smtp = "smtp.broker.local",
            usuario_smtp = usuario,
            senha_smtp = senha,
            emailRemetente = "loja@smtp.broker.local",
            emailDestino = new[] { "pessoa@example.com" },
            mensagem = "<p>Ola</p>",
        });
        Assert.Equal("208", faltou.RootElement[0].GetProperty("codigo").GetString());

        using var negado = await PostarAsync(cliente, "/sendmail/", new
        {
            host_smtp = "smtp.broker.local",
            usuario_smtp = usuario,
            senha_smtp = "errada",
            emailRemetente = "loja@smtp.broker.local",
            emailDestino = new[] { "pessoa@example.com" },
            assunto = "Oi",
            mensagem = "<p>Oi</p>",
        });
        Assert.Equal("207", negado.RootElement[0].GetProperty("codigo").GetString());

        var envios = await cliente.GetFromJsonAsync<JsonElement>("/api/envios?aplicativo=" + id);
        var mensagemId = envios.EnumerateArray().First().GetProperty("mensagemId").GetString()!;
        var chamadas = _fabrica.Email.Chamadas;
        await _fabrica.Services.GetRequiredService<IExecutorDeEnvio>().EnviarAsync(mensagemId, CancellationToken.None);
        Assert.Equal(chamadas, _fabrica.Email.Chamadas);

        using var relatorio = await PostarAsync(cliente, "/report/", new
        {
            token_auth = token,
            dataini = "2020-01-01",
            horaini = "00:00",
            datafim = "2030-01-01",
            horafim = "00:00",
        });
        var linha = relatorio.RootElement.GetProperty("0");
        Assert.Equal("loja@smtp.broker.local", linha.GetProperty("email_de").GetString());
        Assert.Equal("pessoa@example.com", linha.GetProperty("email_para").GetString());
        Assert.False(string.IsNullOrWhiteSpace(linha.GetProperty("msgid").GetString()));
        Assert.Equal("Entregue com sucesso", linha.GetProperty("status").GetString());

        using var consolidado = await PostarAsync(cliente, "/consolidate/", new
        {
            token_auth = token,
            dataini = "2020-01-01",
            horaini = "00:00",
            datafim = "2030-01-01",
            horafim = "00:00",
        });
        Assert.True(int.Parse(consolidado.RootElement[0].GetProperty("Total enviado").GetString()!) >= 1);

        using var adicionado = await PostarAsync(cliente, "/domain/add/", new { token_auth = token, dominio = "loja.example" });
        Assert.Equal("200", adicionado.RootElement.GetProperty("0").GetProperty("codigo").GetString());
        using var checagem = await PostarAsync(cliente, "/domain/check/", new { token_auth = token, dominio = "loja.example" });
        Assert.Equal("loja.example", checagem.RootElement.GetProperty("dominio").GetString());
        Assert.Equal("não validado", checagem.RootElement.GetProperty("validado").GetString());
        Assert.Equal("TXT", checagem.RootElement.GetProperty("spf")[0].GetProperty("type").GetString());
        Assert.Equal("TXT", checagem.RootElement.GetProperty("dkim")[0].GetProperty("type").GetString());
        Assert.StartsWith("_broker.", checagem.RootElement.GetProperty("chave")[0].GetProperty("name").GetString());

        var primeira = await cliente.GetAsync("/api/aplicativos");
        Assert.Equal("miss", primeira.Headers.GetValues("X-Cache").Single());
        Assert.Equal("aplicativos", primeira.Headers.GetValues("X-Cache-Tags").Single());
        var segunda = await cliente.GetAsync("/api/aplicativos");
        Assert.Equal("hit", segunda.Headers.GetValues("X-Cache").Single());
    }

    [Fact]
    public async Task Smtp_entrega_com_as_mesmas_credenciais()
    {
        using var cliente = _fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" })).StatusCode);
        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome = "Smtp" });
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var id = aplicativo.RootElement.GetProperty("id").GetString()!;
        var usuario = aplicativo.RootElement.GetProperty("usuarioSmtp").GetString()!;
        var senha = aplicativo.RootElement.GetProperty("senhaSmtp").GetString()!;
        await cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId = id,
            canal = "email",
            provedor = "falso",
            nome = "unico",
            segredo = "{}",
        });

        var servidor = _fabrica.Services.GetRequiredService<ServidorSmtp>();
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (servidor.Porta == 0 && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", servidor.Porta);
        await using var fluxo = tcp.GetStream();
        Assert.StartsWith("220", await LerAsync(fluxo));
        await EscreverAsync(fluxo, "EHLO teste\r\n");
        await LerAteAsync(fluxo, "250 AUTH");
        await EscreverAsync(fluxo, "AUTH LOGIN\r\n");
        Assert.StartsWith("334", await LerAsync(fluxo));
        await EscreverAsync(fluxo, Convert.ToBase64String(Encoding.UTF8.GetBytes(usuario)) + "\r\n");
        Assert.StartsWith("334", await LerAsync(fluxo));
        await EscreverAsync(fluxo, Convert.ToBase64String(Encoding.UTF8.GetBytes(senha)) + "\r\n");
        Assert.StartsWith("235", await LerAsync(fluxo));
        await EscreverAsync(fluxo, "MAIL FROM:<loja@smtp.broker.local>\r\n");
        Assert.StartsWith("250", await LerAsync(fluxo));
        await EscreverAsync(fluxo, "RCPT TO:<destino@example.com>\r\n");
        Assert.StartsWith("250", await LerAsync(fluxo));
        await EscreverAsync(fluxo, "DATA\r\n");
        Assert.StartsWith("354", await LerAsync(fluxo));
        await EscreverAsync(fluxo, "Subject: Via smtp\r\n\r\nCorpo\r\n.\r\n");
        Assert.StartsWith("250", await LerAsync(fluxo));
    }

    [Fact]
    public async Task Lista_esgotada_devolve_204_e_o_sms_usa_o_mesmo_fallback()
    {
        using var cliente = _fabrica.CreateClient();
        var credenciais = await EntrarECriarAsync(cliente, "Queda");
        await DestinoAsync(cliente, credenciais.Id, "email", "{\"falha\":true}");
        await DestinoAsync(cliente, credenciais.Id, "email", "{\"falha\":true}");
        using var queda = await PostarAsync(cliente, "/sendmail/", Carta(credenciais, "pessoa@example.com"));
        Assert.Equal("204", queda.RootElement[0].GetProperty("codigo").GetString());

        await DestinoAsync(cliente, credenciais.Id, "sms", "{}");
        var sms = await cliente.PostAsJsonAsync("/api/mensagens", new { token_auth = credenciais.Token, para = "+5511999990000", texto = "oi" });
        Assert.Equal(HttpStatusCode.OK, sms.StatusCode);
        using var corpo = JsonDocument.Parse(await sms.Content.ReadAsStringAsync());
        Assert.Equal("200", corpo.RootElement.GetProperty("codigo").GetString());
        Assert.Equal(1, _fabrica.Sms.Chamadas);
    }

    [Fact]
    public async Task Formato_rejeita_tipo_encoding_tamanho_e_limite()
    {
        using var cliente = _fabrica.CreateClient();
        var credenciais = await EntrarECriarAsync(cliente, "Formato");
        var carta = Carta(credenciais, "pessoa@example.com");

        carta["mensagemTipo"] = "xml";
        using var tipo = await PostarAsync(cliente, "/sendmail/", carta);
        Assert.Equal("212", tipo.RootElement[0].GetProperty("codigo").GetString());

        carta.Remove("mensagemTipo");
        carta["mensagemEncoding"] = "base64";
        carta["mensagem"] = "%%%";
        using var encoding = await PostarAsync(cliente, "/sendmail/", carta);
        Assert.Equal("211", encoding.RootElement[0].GetProperty("codigo").GetString());

        carta.Remove("mensagemEncoding");
        carta["mensagem"] = new string('a', (10 * 1024 * 1024) + 1);
        using var tamanho = await PostarAsync(cliente, "/sendmail/", carta);
        Assert.Equal("215", tamanho.RootElement[0].GetProperty("codigo").GetString());

        var muitos = Enumerable.Range(0, 101).Select(indice => "p" + indice + "@example.com").ToArray();
        using var limite = await PostarAsync(cliente, "/sendmail/", Carta(credenciais, muitos));
        Assert.Equal("221", limite.RootElement[0].GetProperty("codigo").GetString());
        using var copia = await PostarAsync(cliente, "/sendmail/", Carta(credenciais, "pessoa@example.com", muitos, null));
        Assert.Equal("222", copia.RootElement[0].GetProperty("codigo").GetString());
        using var oculta = await PostarAsync(cliente, "/sendmail/", Carta(credenciais, "pessoa@example.com", null, muitos));
        Assert.Equal("223", oculta.RootElement[0].GetProperty("codigo").GetString());
    }

    private async Task<Credenciais> EntrarECriarAsync(HttpClient cliente, string nome)
    {
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" })).StatusCode);
        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome });
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var raiz = aplicativo.RootElement;
        return new Credenciais(
            raiz.GetProperty("id").GetString()!,
            raiz.GetProperty("usuarioSmtp").GetString()!,
            raiz.GetProperty("senhaSmtp").GetString()!,
            raiz.GetProperty("tokenAuth").GetString()!);
    }

    private static Task<HttpResponseMessage> DestinoAsync(HttpClient cliente, string aplicativoId, string canal, string segredo)
    {
        return cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId,
            canal,
            provedor = "falso",
            nome = canal,
            segredo,
        });
    }

    private static Dictionary<string, object> Carta(Credenciais credenciais, string destino)
    {
        return Carta(credenciais, new[] { destino }, null, null);
    }

    private static Dictionary<string, object> Carta(Credenciais credenciais, string destino, string[]? copias, string[]? ocultas)
    {
        return Carta(credenciais, new[] { destino }, copias, ocultas);
    }

    private static Dictionary<string, object> Carta(Credenciais credenciais, string[] destinos)
    {
        return Carta(credenciais, destinos, null, null);
    }

    private static Dictionary<string, object> Carta(Credenciais credenciais, string[] destinos, string[]? copias, string[]? ocultas)
    {
        var carta = new Dictionary<string, object>
        {
            ["host_smtp"] = "smtp.broker.local",
            ["usuario_smtp"] = credenciais.Usuario,
            ["senha_smtp"] = credenciais.Senha,
            ["emailRemetente"] = "loja@smtp.broker.local",
            ["emailDestino"] = destinos,
            ["assunto"] = "Oi",
            ["mensagem"] = "<p>Oi</p>",
        };
        if (copias is not null)
        {
            carta["emailDestinoCopia"] = copias;
        }

        if (ocultas is not null)
        {
            carta["emailDestinoCopiaOculta"] = ocultas;
        }

        return carta;
    }

    private sealed record Credenciais(string Id, string Usuario, string Senha, string Token);

    private static StringContent Json(object corpo)
    {
        return new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
    }

    private static async Task<JsonDocument> PostarAsync(HttpClient cliente, string caminho, object corpo)
    {
        var resposta = await cliente.PostAsync(caminho, new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json"));
        var texto = await resposta.Content.ReadAsStringAsync();
        var documento = JsonDocument.Parse(texto);
        return documento;
    }

    private static async Task EscreverAsync(NetworkStream fluxo, string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        await fluxo.WriteAsync(bytes);
    }

    private static async Task<string> LerAsync(NetworkStream fluxo)
    {
        var buffer = new byte[1024];
        var lido = await fluxo.ReadAsync(buffer);
        return Encoding.UTF8.GetString(buffer, 0, lido);
    }

    private static async Task LerAteAsync(NetworkStream fluxo, string trecho)
    {
        var texto = "";
        var limite = DateTime.UtcNow.AddSeconds(2);
        while (!texto.Contains(trecho, StringComparison.Ordinal) && DateTime.UtcNow < limite)
        {
            texto += await LerAsync(fluxo);
        }

        Assert.Contains(trecho, texto);
    }
}
