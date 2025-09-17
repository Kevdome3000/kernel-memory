using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.GraphRAG.Configuration;
using Microsoft.KernelMemory.GraphRAG.SearchClients;
using Microsoft.KernelMemory.GraphRAG.Services;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions for KernelMemoryBuilder and generic DI
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Inject GraphRAG services and search clients
    /// </summary>
    public static IServiceCollection AddGraphRag(this IServiceCollection services, GraphRagConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Validate configuration on startup
        config.Validate();

        // Register configuration
        services.AddSingleton(config);

        // Register core services
        services.AddSingleton<EntityExtractionService>();
        services.AddSingleton<CommunityDetectionService>();
        services.AddSingleton<TextChunkingService>();
        services.AddSingleton<GraphSearchService>();

        // Register search client
        services.AddSingleton<GraphRagSearchClient>(serviceProvider =>
        {
            ILoggerFactory? loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            ILogger<GraphRagSearchClient>? logger = loggerFactory?.CreateLogger<GraphRagSearchClient>();

            var graphSearchService = serviceProvider.GetRequiredService<GraphSearchService>();
            var communityDetectionService = serviceProvider.GetRequiredService<CommunityDetectionService>();

            return new GraphRagSearchClient(
                graphSearchService,
                communityDetectionService);
        });

        return services;
    }


    /// <summary>
    /// Add GraphRAG pipeline handlers to the memory pipeline
    /// </summary>
    public static IServiceCollection AddGraphRagPipelineHandlers(this IServiceCollection services)
    {
        // Pipeline handlers will be implemented in the next phase
        // services.AddSingleton<IMemoryPipelineHandler, GraphRagEntityExtractionHandler>();
        // services.AddSingleton<IMemoryPipelineHandler, GraphRagCommunityDetectionHandler>();
        // services.AddSingleton<IMemoryPipelineHandler, GraphRagCommunitySummarizationHandler>();

        return services;
    }


    /// <summary>
    /// Configure GraphRAG with default settings
    /// </summary>
    public static IServiceCollection AddGraphRagWithDefaults(this IServiceCollection services)
    {
        var config = new GraphRagConfig();
        return services.AddGraphRag(config);
    }


    /// <summary>
    /// Configure GraphRAG with custom configuration action
    /// </summary>
    public static IServiceCollection AddGraphRag(this IServiceCollection services, Action<GraphRagConfig> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var config = new GraphRagConfig();
        configureOptions(config);

        return services.AddGraphRag(config);
    }
}
