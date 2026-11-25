using broker.Correio;
using broker.Health;
using broker.Health.Adapters;
using broker.Persistence.Adapters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace broker.Infrastructure;

public static class InfraestruturaExtensions
{
    public static IServiceCollection AddProtecaoDoBroker(this IServiceCollection services, string pasta)
    {
        Directory.CreateDirectory(pasta);
        services.AddDataProtection()
            .SetApplicationName("broker")
            .PersistKeysToFileSystem(new DirectoryInfo(pasta));
        services.AddSingleton<IProtecaoDeSegredo, ProtecaoDeSegredo>();
        return services;
    }

    public static IServiceCollection AddInfraestruturaEmMemoria(this IServiceCollection services)
    {
        services.AddSingleton<ISondaDeInfraestrutura>(new SondaEmMemoria(NomesDeSonda.Banco));
        services.AddSingleton<AcervoEmMemoria>();
        services.AddSingleton<IAcervo>(provedor => provedor.GetRequiredService<AcervoEmMemoria>());
        return services;
    }

    public static IServiceCollection AddRepositorioSql(this IServiceCollection services, OpcoesDeBanco opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<IFabricaDeConexao, FabricaDeConexao>();
        services.AddSingleton<IDialetoSql>(DialetosSql.Para(opcoes.ProvedorInvariante));
        services.AddSingleton<SessaoSql>();
        services.AddSingleton<SondaDeBanco>();
        services.AddSingleton<ISondaDeInfraestrutura>(provedor => provedor.GetRequiredService<SondaDeBanco>());
        services.AddSingleton<AcervoSql>();
        services.AddSingleton<IAcervo>(provedor => provedor.GetRequiredService<AcervoSql>());
        return services;
    }
}
