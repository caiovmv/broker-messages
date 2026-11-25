using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using broker.Health;
using Microsoft.Extensions.Hosting;

namespace broker.Correio;

public sealed class ServidorSmtp : BackgroundService, ISondaDeInfraestrutura
{
    private readonly ServicoDeCorreio _correio;
    private readonly bool _escutar;
    private readonly int _portaPedida;
    private readonly X509Certificate2? _certificado;
    private TcpListener? _ouvinte;

    public ServidorSmtp(ServicoDeCorreio correio, bool escutar, int porta, string? certificado, string? senha)
    {
        _correio = correio;
        _escutar = escutar;
        _portaPedida = porta;
        if (!string.IsNullOrWhiteSpace(certificado))
        {
            _certificado = string.IsNullOrEmpty(senha)
                ? X509CertificateLoader.LoadCertificateFromFile(certificado)
                : X509CertificateLoader.LoadPkcs12FromFile(certificado, senha);
        }
    }

    public int Porta { get; private set; }

    public string Nome => NomesDeSonda.Smtp;

    public Task<EstadoDaSonda> VerificarAsync(CancellationToken cancellationToken)
    {
        if (!_escutar)
        {
            return Task.FromResult(new EstadoDaSonda(true, "desligado"));
        }

        var ativo = _ouvinte is not null;
        return Task.FromResult(new EstadoDaSonda(ativo, ativo ? "escuta " + Porta : "sem escuta"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_escutar)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        _ouvinte = new TcpListener(IPAddress.Any, _portaPedida);
        _ouvinte.Start();
        Porta = ((IPEndPoint)_ouvinte.LocalEndpoint).Port;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var cliente = await _ouvinte.AcceptTcpClientAsync(stoppingToken);
                _ = AtenderAsync(cliente, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _ouvinte.Stop();
        }
    }

    private async Task AtenderAsync(TcpClient cliente, CancellationToken cancellationToken)
    {
        using (cliente)
        await using (var rede = cliente.GetStream())
        {
            Stream fluxo = rede;
            var usuario = "";
            var senha = "";
            string? de = null;
            var para = new List<string>();
            await EscreverAsync(fluxo, "220 broker ESMTP\r\n", cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var linha = await LerLinhaAsync(fluxo, cancellationToken);
                if (linha is null)
                {
                    break;
                }

                var comando = linha.Trim();
                var verbo = comando.Split(' ', 2)[0].ToUpperInvariant();
                if (verbo is "EHLO" or "HELO")
                {
                    var extra = _certificado is null ? "" : "250-STARTTLS\r\n";
                    await EscreverAsync(fluxo, "250-broker\r\n" + extra + "250 AUTH LOGIN PLAIN\r\n", cancellationToken);
                }
                else if (verbo == "STARTTLS")
                {
                    if (_certificado is null)
                    {
                        await EscreverAsync(fluxo, "454 TLS indisponivel\r\n", cancellationToken);
                        continue;
                    }

                    await EscreverAsync(fluxo, "220 pronto\r\n", cancellationToken);
                    var ssl = new SslStream(rede, leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificado,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, cancellationToken);
                    fluxo = ssl;
                }
                else if (verbo == "AUTH")
                {
                    var resto = comando.Length > 5 ? comando[5..] : "";
                    if (resto.StartsWith("PLAIN", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = resto["PLAIN".Length..].Trim();
                        if (token.Length == 0)
                        {
                            await EscreverAsync(fluxo, "334 \r\n", cancellationToken);
                            token = (await LerLinhaAsync(fluxo, cancellationToken) ?? "").Trim();
                        }

                        var claro = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('\0');
                        if (claro.Length >= 3)
                        {
                            usuario = claro[1];
                            senha = claro[2];
                        }

                        await EscreverAsync(fluxo, "235 autenticado\r\n", cancellationToken);
                    }
                    else
                    {
                        await EscreverAsync(fluxo, "334 VXNlcm5hbWU6\r\n", cancellationToken);
                        var usuarioB64 = await LerLinhaAsync(fluxo, cancellationToken) ?? "";
                        await EscreverAsync(fluxo, "334 UGFzc3dvcmQ6\r\n", cancellationToken);
                        var senhaB64 = await LerLinhaAsync(fluxo, cancellationToken) ?? "";
                        usuario = Encoding.UTF8.GetString(Convert.FromBase64String(usuarioB64.Trim()));
                        senha = Encoding.UTF8.GetString(Convert.FromBase64String(senhaB64.Trim()));
                        await EscreverAsync(fluxo, "235 autenticado\r\n", cancellationToken);
                    }
                }
                else if (verbo == "MAIL")
                {
                    de = Entre(comando, '<', '>');
                    await EscreverAsync(fluxo, "250 ok\r\n", cancellationToken);
                }
                else if (verbo == "RCPT")
                {
                    var endereco = Entre(comando, '<', '>');
                    if (!string.IsNullOrWhiteSpace(endereco))
                    {
                        para.Add(endereco);
                    }

                    await EscreverAsync(fluxo, "250 ok\r\n", cancellationToken);
                }
                else if (verbo == "DATA")
                {
                    await EscreverAsync(fluxo, "354 continue\r\n", cancellationToken);
                    var dados = await LerDadosAsync(fluxo, cancellationToken);
                    var (assunto, corpo, remetente) = Partir(dados);
                    var json = JsonSerializer.Serialize(new
                    {
                        host_smtp = _correio.HostSmtp,
                        usuario_smtp = usuario,
                        senha_smtp = senha,
                        emailRemetente = string.IsNullOrWhiteSpace(de) ? remetente : de,
                        emailDestino = para,
                        assunto = string.IsNullOrWhiteSpace(assunto) ? "(sem assunto)" : assunto,
                        mensagem = string.IsNullOrWhiteSpace(corpo) ? " " : corpo,
                    });
                    using var documento = JsonDocument.Parse(json);
                    var resultado = await _correio.EnviarEmailAsync(documento.RootElement, exigirHost: false, cancellationToken);
                    var ok = resultado.All(item => item.codigo == "200");
                    await EscreverAsync(fluxo, ok ? "250 enviado\r\n" : "550 recusado\r\n", cancellationToken);
                    de = null;
                    para.Clear();
                }
                else if (verbo == "RSET")
                {
                    de = null;
                    para.Clear();
                    await EscreverAsync(fluxo, "250 ok\r\n", cancellationToken);
                }
                else if (verbo == "QUIT")
                {
                    await EscreverAsync(fluxo, "221 tchau\r\n", cancellationToken);
                    break;
                }
                else if (verbo == "NOOP")
                {
                    await EscreverAsync(fluxo, "250 ok\r\n", cancellationToken);
                }
                else
                {
                    await EscreverAsync(fluxo, "502 comando nao implementado\r\n", cancellationToken);
                }
            }
        }
    }

    private static string? Entre(string texto, char abre, char fecha)
    {
        var inicio = texto.IndexOf(abre);
        var fim = texto.IndexOf(fecha);
        if (inicio < 0 || fim <= inicio)
        {
            return null;
        }

        return texto[(inicio + 1)..fim].Trim();
    }

    private static (string Assunto, string Corpo, string Remetente) Partir(string dados)
    {
        var divisor = dados.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var cabecalho = divisor < 0 ? dados : dados[..divisor];
        var corpo = divisor < 0 ? "" : dados[(divisor + 4)..];
        var assunto = "";
        var remetente = "";
        foreach (var linha in cabecalho.Split("\r\n"))
        {
            if (linha.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
            {
                assunto = linha["Subject:".Length..].Trim();
            }

            if (linha.StartsWith("From:", StringComparison.OrdinalIgnoreCase))
            {
                remetente = Entre(linha, '<', '>') ?? linha["From:".Length..].Trim();
            }
        }

        return (assunto, corpo, remetente);
    }

    private static async Task<string?> LerLinhaAsync(Stream fluxo, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var um = new byte[1];
        while (true)
        {
            var lido = await fluxo.ReadAsync(um, cancellationToken);
            if (lido == 0)
            {
                return buffer.Count == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (um[0] == (byte)'\n')
            {
                break;
            }

            if (um[0] != (byte)'\r')
            {
                buffer.Add(um[0]);
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task<string> LerDadosAsync(Stream fluxo, CancellationToken cancellationToken)
    {
        var texto = new StringBuilder();
        while (true)
        {
            var linha = await LerLinhaAsync(fluxo, cancellationToken);
            if (linha is null || linha == ".")
            {
                break;
            }

            texto.Append(linha.StartsWith("..", StringComparison.Ordinal) ? linha[1..] : linha);
            texto.Append("\r\n");
        }

        return texto.ToString();
    }

    private static Task EscreverAsync(Stream fluxo, string texto, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        return fluxo.WriteAsync(bytes, cancellationToken).AsTask();
    }
}
