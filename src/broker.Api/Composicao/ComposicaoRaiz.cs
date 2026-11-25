using broker.Caching.Adapters;
using broker.Correio;
using broker.Health;
using broker.Health.Adapters;
using broker.Infrastructure;
using broker.Observability;
using broker.Observability.Adapters;
using broker.Persistence.Adapters;
using broker.Workflows.Adapters;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace broker.Api;

public static class ComposicaoRaiz
{
    public static IServiceCollection AddEsqueleto(this IServiceCollection services, IConfiguration configuracao)
    {
        var saude = services.AddHealthChecks();
        saude.AddCheck("processo", () => HealthCheckResult.Healthy("ok"), tags: new[] { "live" });
        AdicionarBanco(services, configuracao);
        AdicionarCache(services, configuracao);
        AdicionarCorreio(services, configuracao);
        foreach (var nome in NomesDeSonda.Todos)
        {
            saude.Add(new HealthCheckRegistration(
                nome,
                provedor => new AdaptadorDeSonda(
                    provedor.GetServices<ISondaDeInfraestrutura>().First(sonda => sonda.Nome == nome),
                    provedor.GetRequiredService<ITelemetry>()),
                HealthStatus.Unhealthy,
                new[] { "ready", "complete" },
                TimeSpan.FromSeconds(5)));
        }

        services.AddControllers();
        services.AddCors(opcoes => opcoes.AddDefaultPolicy(politica => politica
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
        services.AddSingleton<IModuloDeRotas, ModuloDeRotasDeSaude>();
        services.AddSingleton<IModuloDeRotas, RotasMailgrid>();
        services.AddSingleton<IModuloDeRotas, RotasDoConsole>();
        services.AddSingleton<IModuloDeRotas, RotasDeWebhook>();
        services.AddSingleton<ITelemetry, TelemetriaDeServico>();
        AdicionarTelemetria(services, configuracao);
        return services;
    }

    private static void AdicionarCorreio(IServiceCollection services, IConfiguration configuracao)
    {
        services.AddHttpClient("provedores");
        var pastaDeChaves = configuracao["Broker:PastaDeChaves"];
        if (string.IsNullOrWhiteSpace(pastaDeChaves))
        {
            pastaDeChaves = Path.Combine(AppContext.BaseDirectory, "chaves");
        }

        services.AddProtecaoDoBroker(pastaDeChaves);
        services.AddSingleton<ProvedorSendGrid>();
        services.AddSingleton<IProvedorDeCanal>(provedor => provedor.GetRequiredService<ProvedorSendGrid>());
        services.AddSingleton<IRegistroDeDominio>(provedor => provedor.GetRequiredService<ProvedorSendGrid>());
        services.AddSingleton<IProvedorDeCanal, ProvedorMailgrid>();
        services.AddSingleton<IProvedorDeCanal, ProvedorTwilio>();
        services.AddSingleton<IConsultaDeDns, ConsultaDns>();
        services.AddSingleton<IExecutorDeEnvio, ExecutorDeEnvio>();
        var endpoint = configuracao["Workflows:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddSingleton<DurabilidadeEmMemoria>();
            services.AddSingleton<broker.Correio.IDurabilidade>(provedor => provedor.GetRequiredService<DurabilidadeEmMemoria>());
            services.AddSingleton<ISondaDeInfraestrutura>(provedor => provedor.GetRequiredService<DurabilidadeEmMemoria>());
        }
        else
        {
            var opcoes = DestinoDeWorkflow.Montar(endpoint, configuracao["Workflows:Namespace"], configuracao["Workflows:Queue"]);
            services.AddSingleton(opcoes);
            services.AddSingleton<AtividadesDeBroker>();
            services.AddSingleton<DurabilidadeTemporal>();
            services.AddSingleton<broker.Correio.IDurabilidade>(provedor => provedor.GetRequiredService<DurabilidadeTemporal>());
            services.AddSingleton<ISondaDeInfraestrutura>(provedor => provedor.GetRequiredService<DurabilidadeTemporal>());
            services.AddHostedService<TrabalhadorTemporal>();
        }

        var host = string.IsNullOrWhiteSpace(configuracao["Broker:HostSmtp"]) ? "localhost" : configuracao["Broker:HostSmtp"]!.Trim();
        var porta = 587;
        if (int.TryParse(configuracao["Broker:PortaSmtp"], out var lida) && lida > 0)
        {
            porta = lida;
        }

        services.AddSingleton(provedor => new ServicoDeCorreio(
            provedor.GetRequiredService<IAcervo>(),
            provedor.GetRequiredService<IProtecaoDeSegredo>(),
            provedor.GetRequiredService<broker.Correio.IDurabilidade>(),
            host,
            porta));
        var escutar = string.Equals(configuracao["Smtp:Escutar"], "true", StringComparison.OrdinalIgnoreCase);
        var portaSmtp = int.TryParse(configuracao["Smtp:Porta"], out var portaLida) ? portaLida : porta;
        services.AddSingleton(provedor => new ServidorSmtp(
            provedor.GetRequiredService<ServicoDeCorreio>(),
            escutar,
            portaSmtp,
            configuracao["Smtp:Certificado"],
            configuracao["Smtp:SenhaDoCertificado"]));
        services.AddSingleton<ISondaDeInfraestrutura>(provedor => provedor.GetRequiredService<ServidorSmtp>());
        services.AddHostedService(provedor => provedor.GetRequiredService<ServidorSmtp>());
        services.AddSingleton(new SessoesDoOperador(configuracao["Broker:OperadorUsuario"] ?? "", configuracao["Broker:OperadorSenha"] ?? ""));
    }

    private static void AdicionarTelemetria(IServiceCollection services, IConfiguration configuracao)
    {
        var endpoint = configuracao["Observability:OtlpEndpoint"];
        var exportar = !string.IsNullOrWhiteSpace(endpoint);
        services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService("broker"))
            .WithTracing(rastreio =>
            {
                rastreio.AddSource(TelemetryCatalog.Fonte);
                rastreio.AddAspNetCoreInstrumentation();
                rastreio.AddHttpClientInstrumentation();
                rastreio.AddProcessor(new ProcessadorDeRedacao());
                if (exportar)
                {
                    rastreio.AddOtlpExporter(opcoes => opcoes.Endpoint = new Uri(endpoint!));
                }
            })
            .WithMetrics(medidas =>
            {
                medidas.AddMeter(TelemetryCatalog.Medidor);
                medidas.AddAspNetCoreInstrumentation();
                medidas.AddHttpClientInstrumentation();
                if (exportar)
                {
                    medidas.AddOtlpExporter(opcoes => opcoes.Endpoint = new Uri(endpoint!));
                }
            })
            .WithLogging(
                null,
                opcoes =>
                {
                    opcoes.IncludeScopes = true;
                    opcoes.IncludeFormattedMessage = true;
                    opcoes.AddProcessor(new ProcessadorDeLog());
                    if (exportar)
                    {
                        opcoes.AddOtlpExporter(exportacao => exportacao.Endpoint = new Uri(endpoint!));
                    }
                });
    }

    private static void AdicionarBanco(IServiceCollection services, IConfiguration configuracao)
    {
        var connectionString = configuracao["Database:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddInfraestruturaEmMemoria();
            return;
        }

        var invariante = ProvedoresConhecidos.Invariante(configuracao["Database:Provider"] ?? string.Empty);
        RegistroDeProvedores.Registrar(invariante);
        services.AddRepositorioSql(new OpcoesDeBanco(invariante, connectionString));
    }

    private static void AdicionarCache(IServiceCollection services, IConfiguration configuracao)
    {
        var connectionString = configuracao["Caching:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddInMemoryCache();
            return;
        }

        var provedor = ProvedoresDeCache.Canonico(configuracao["Caching:Provider"] ?? string.Empty);
        if (provedor != ProvedoresDeCache.Redis && provedor != ProvedoresDeCache.Valkey)
        {
            throw new InvalidOperationException("Cache '" + provedor + "' nao e Redis nem Valkey.");
        }

        var prefixo = (configuracao["Caching:Prefix"] ?? string.Empty).Trim();
        services.AddValkeyCache(connectionString, prefixo);
    }
}
