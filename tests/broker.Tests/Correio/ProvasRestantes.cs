using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using broker.Correio;
using broker.Workflows.Adapters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

namespace broker.Tests.Correio;

[Collection("broker")]
public sealed class CadastroDeDominioNaoEspera
{
    [Fact]
    public async Task Add_responde_antes_da_consulta_dns()
    {
        await using var fabrica = new FabricaDnsLento();
        using var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" })).StatusCode);
        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome = "Dominio" });
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var token = aplicativo.RootElement.GetProperty("tokenAuth").GetString();
        var relogio = Stopwatch.StartNew();
        var resposta = await cliente.PostAsJsonAsync("/domain/add/", new { token_auth = token, dominio = "lento.example" });
        relogio.Stop();
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(1), relogio.Elapsed.ToString());
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal("200", corpo.RootElement.GetProperty("0").GetProperty("codigo").GetString());
    }
}

public sealed class FabricaDnsLento : FabricaDoBroker
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(servicos => servicos.AddSingleton<IConsultaDeDns, DnsLento>());
    }

    private sealed class DnsLento : IConsultaDeDns
    {
        public async Task<bool> ContemTxtAsync(string nome, string trecho, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            return false;
        }
    }
}

[Collection("broker")]
public sealed class SmtpComStartTls
{
    [Fact]
    public async Task Ehlo_anuncia_starttls_e_a_autenticacao_segue_no_canal_seguro()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "broker-smtp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, "smtp.pfx");
        File.WriteAllBytes(arquivo, Certificado());
        await using var fabrica = new FabricaDoBroker(new Dictionary<string, string?>
        {
            ["Smtp__Certificado"] = arquivo,
            ["Smtp__SenhaDoCertificado"] = "senha",
        });
        using var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" })).StatusCode);
        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome = "Tls" });
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var usuario = aplicativo.RootElement.GetProperty("usuarioSmtp").GetString()!;
        var senha = aplicativo.RootElement.GetProperty("senhaSmtp").GetString()!;
        await cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId = aplicativo.RootElement.GetProperty("id").GetString(),
            canal = "email",
            provedor = "falso",
            nome = "unico",
            segredo = "{}",
        });

        var servidor = fabrica.Services.GetRequiredService<ServidorSmtp>();
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (servidor.Porta == 0 && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", servidor.Porta);
        await using var rede = tcp.GetStream();
        Assert.StartsWith("220", await LerAsync(rede));
        await EscreverAsync(rede, "EHLO teste\r\n");
        var anuncio = await LerAteAsync(rede, "250 AUTH");
        Assert.Contains("STARTTLS", anuncio, StringComparison.Ordinal);
        await EscreverAsync(rede, "STARTTLS\r\n");
        Assert.StartsWith("220", await LerAsync(rede));
        await using var ssl = new SslStream(rede, leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync("localhost");
        await EscreverAsync(ssl, "EHLO teste\r\n");
        await LerAteAsync(ssl, "250 AUTH");
        await EscreverAsync(ssl, "AUTH LOGIN\r\n");
        Assert.StartsWith("334", await LerAsync(ssl));
        await EscreverAsync(ssl, Convert.ToBase64String(Encoding.UTF8.GetBytes(usuario)) + "\r\n");
        Assert.StartsWith("334", await LerAsync(ssl));
        await EscreverAsync(ssl, Convert.ToBase64String(Encoding.UTF8.GetBytes(senha)) + "\r\n");
        Assert.StartsWith("235", await LerAsync(ssl));
    }

    private static byte[] Certificado()
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        return certificado.Export(X509ContentType.Pkcs12, "senha");
    }

    private static async Task EscreverAsync(Stream fluxo, string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        await fluxo.WriteAsync(bytes);
    }

    private static async Task<string> LerAsync(Stream fluxo)
    {
        var buffer = new byte[1024];
        var lido = await fluxo.ReadAsync(buffer);
        return Encoding.UTF8.GetString(buffer, 0, lido);
    }

    private static async Task<string> LerAteAsync(Stream fluxo, string trecho)
    {
        var texto = "";
        var limite = DateTime.UtcNow.AddSeconds(2);
        while (!texto.Contains(trecho, StringComparison.Ordinal) && DateTime.UtcNow < limite)
        {
            texto += await LerAsync(fluxo);
        }

        return texto;
    }
}

public sealed class ChavesSobrevivem
{
    [Fact]
    public void Um_segundo_processo_le_o_segredo_gravado_na_pasta()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "broker-chaves-" + Guid.NewGuid().ToString("N"));
        var primeira = new ProtecaoDeSegredo(DataProtectionProvider.Create(new DirectoryInfo(pasta)));
        var protegido = primeira.Proteger("senha-smtp");
        var segunda = new ProtecaoDeSegredo(DataProtectionProvider.Create(new DirectoryInfo(pasta)));
        Assert.Equal("senha-smtp", segunda.Revelar(protegido));
    }
}

[Collection("broker")]
public sealed class AcervoSqlSqlite
{
    [Fact]
    public async Task Sqlite_grava_aplicativo_destino_e_envio()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "broker-" + Guid.NewGuid().ToString("N") + ".db");
        await using var fabrica = new FabricaDoBroker(new Dictionary<string, string?>
        {
            ["Database__Provider"] = "Sqlite",
            ["Database__ConnectionString"] = "Data Source=" + caminho,
        });
        using var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" })).StatusCode);
        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome = "Sql" });
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var id = aplicativo.RootElement.GetProperty("id").GetString();
        var usuario = aplicativo.RootElement.GetProperty("usuarioSmtp").GetString();
        var senha = aplicativo.RootElement.GetProperty("senhaSmtp").GetString();
        await cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId = id,
            canal = "email",
            provedor = "falso",
            nome = "unico",
            segredo = "{}",
        });
        var enviado = await cliente.PostAsJsonAsync("/sendmail/", new
        {
            host_smtp = "smtp.broker.local",
            usuario_smtp = usuario,
            senha_smtp = senha,
            emailRemetente = "loja@smtp.broker.local",
            emailDestino = new[] { "pessoa@example.com" },
            assunto = "Sql",
            mensagem = "<p>Sql</p>",
        });
        Assert.Equal(HttpStatusCode.OK, enviado.StatusCode);
        using var corpo = JsonDocument.Parse(await enviado.Content.ReadAsStringAsync());
        Assert.Equal("200", corpo.RootElement[0].GetProperty("codigo").GetString());
        var lista = await cliente.GetFromJsonAsync<JsonElement>("/api/aplicativos");
        Assert.Contains(lista.EnumerateArray(), item => item.GetProperty("nome").GetString() == "Sql");
    }
}

[Collection("broker")]
public sealed class AcervoNosMotores
{
    [Fact]
    public Task Postgres()
    {
        return RodarAsync("BROKER_TEST_POSTGRES", "PostgreSQL");
    }

    [Fact]
    public Task MySql()
    {
        return RodarAsync("BROKER_TEST_MYSQL", "MySQL");
    }

    [Fact]
    public Task SqlServer()
    {
        return RodarAsync("BROKER_TEST_SQLSERVER", "SqlServer");
    }

    private static async Task RodarAsync(string variavel, string provedor)
    {
        var conexao = Environment.GetEnvironmentVariable(variavel);
        if (string.IsNullOrWhiteSpace(conexao))
        {
            return;
        }
        await using var fabrica = new FabricaDoBroker(new Dictionary<string, string?>
        {
            ["Database__Provider"] = provedor,
            ["Database__ConnectionString"] = conexao,
        });
        using var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/sessao", new { usuario = "operador", senha = "segredo-teste" })).StatusCode);
        var criado = await cliente.PostAsJsonAsync("/api/aplicativos", new { nome = provedor });
        Assert.Equal(HttpStatusCode.OK, criado.StatusCode);
        using var aplicativo = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var id = aplicativo.RootElement.GetProperty("id").GetString();
        await cliente.PostAsJsonAsync("/api/destinos", new
        {
            aplicativoId = id,
            canal = "email",
            provedor = "falso",
            nome = "unico",
            segredo = "{}",
        });
        var enviado = await cliente.PostAsJsonAsync("/sendmail/", new
        {
            host_smtp = "smtp.broker.local",
            usuario_smtp = aplicativo.RootElement.GetProperty("usuarioSmtp").GetString(),
            senha_smtp = aplicativo.RootElement.GetProperty("senhaSmtp").GetString(),
            emailRemetente = "loja@smtp.broker.local",
            emailDestino = new[] { "pessoa@example.com" },
            assunto = provedor,
            mensagem = "<p>ok</p>",
        });
        using var corpo = JsonDocument.Parse(await enviado.Content.ReadAsStringAsync());
        Assert.Equal("200", corpo.RootElement[0].GetProperty("codigo").GetString());
    }
}

public sealed class WorkflowTemporal
{
    [Fact]
    public async Task Retry_do_fluxo_nao_envia_de_novo()
    {
        await using var ambiente = await WorkflowEnvironment.StartTimeSkippingAsync();
        var pasta = Path.Combine(Path.GetTempPath(), "broker-temporal-" + Guid.NewGuid().ToString("N"));
        var acervo = new AcervoEmMemoria();
        var protecao = new ProtecaoDeSegredo(DataProtectionProvider.Create(new DirectoryInfo(pasta)));
        var provedor = new ProvedorContado("falso", "email");
        var executor = new ExecutorDeEnvio(acervo, protecao, new[] { provedor }, new DnsParado(), Array.Empty<IRegistroDeDominio>());
        var aplicativo = "app";
        var destino = "des";
        var mensagem = "msg";
        var entrega = "ent";
        await acervo.InserirAplicativoAsync(new Aplicativo { Id = aplicativo, Nome = "Temporal", UsuarioSmtp = "u", CriadoEm = "2026-01-01T00:00:00Z" }, CancellationToken.None);
        await acervo.InserirDestinoAsync(new Destino
        {
            Id = destino,
            AplicativoId = aplicativo,
            Canal = "email",
            Provedor = "falso",
            Ordem = 1,
            Nome = "unico",
            SegredoProtegido = protecao.Proteger("{}"),
        }, CancellationToken.None);
        await acervo.InserirMensagemAsync(
            new MensagemGravada
            {
                Id = mensagem,
                AplicativoId = aplicativo,
                Canal = "email",
                MsgId = "m",
                Remetente = "loja@smtp.broker.local",
                Assunto = "Oi",
                Corpo = "<p>Oi</p>",
                CriadoEm = "2026-01-01T00:00:00Z",
            },
            new[]
            {
                new EntregaGravada
                {
                    Id = entrega,
                    MensagemId = mensagem,
                    Destinatario = "pessoa@example.com",
                    Codigo = "",
                    Status = "",
                    AtualizadoEm = "2026-01-01T00:00:00Z",
                },
            },
            CancellationToken.None);

        var atividades = new AtividadesDeBroker(executor);
        using var trabalhador = new TemporalWorker(
            ambiente.Client,
            new TemporalWorkerOptions("broker-teste").AddWorkflow<FluxoDeEnvio>().AddAllActivities(atividades));
        await trabalhador.ExecuteAsync(async () =>
        {
            await ambiente.Client.ExecuteWorkflowAsync(
                (FluxoDeEnvio fluxo) => fluxo.Executar(mensagem),
                new WorkflowOptions(id: "envio-" + mensagem, taskQueue: "broker-teste"));
            Assert.Equal(1, provedor.Chamadas);
            await ambiente.Client.ExecuteWorkflowAsync(
                (FluxoDeEnvio fluxo) => fluxo.Executar(mensagem),
                new WorkflowOptions(id: "envio-" + mensagem + "-de-novo", taskQueue: "broker-teste"));
            Assert.Equal(1, provedor.Chamadas);
        });
    }

    private sealed class DnsParado : IConsultaDeDns
    {
        public Task<bool> ContemTxtAsync(string nome, string trecho, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }
}
