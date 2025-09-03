using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Neoo4j;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extensions for KernelMemoryBuilder and generic DI
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Inject Neo4j as the default implementation of IMemoryDb
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static IServiceCollection AddNeo4jAsVectorDb(this IServiceCollection services, Neo4jConfig neo4JConfig)
    {
        ArgumentNullException.ThrowIfNull(neo4JConfig);

        services.AddSingleton(sp => Neo4jDriverFactory.BuildDriver(neo4JConfig, sp.GetRequiredService<ILogger>()));

        return services
            .AddSingleton(neo4JConfig)
            .AddSingleton<IMemoryDb, Neo4jMemory>();
    }
}
