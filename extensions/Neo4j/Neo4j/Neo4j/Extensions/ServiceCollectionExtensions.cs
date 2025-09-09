using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Neo4j;

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
    public static IServiceCollection AddNeo4jAsMemoryDb(this IServiceCollection services, Neo4jConfig neo4JConfig)
    {
        ArgumentNullException.ThrowIfNull(neo4JConfig);

        // Validate configuration on startup
        neo4JConfig.Validate();

        services.AddSingleton<IMemoryDb>(serviceProvider =>
        {
            ILoggerFactory? loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            ILogger<Neo4jMemory>? logger = loggerFactory?.CreateLogger<Neo4jMemory>();
            ITextEmbeddingGenerator embeddingGenerator = serviceProvider.GetRequiredService<ITextEmbeddingGenerator>();

            return new Neo4jMemory(neo4JConfig,
                embeddingGenerator,
                logger,
                loggerFactory);
        });

        return services.AddSingleton(neo4JConfig);
    }
}
