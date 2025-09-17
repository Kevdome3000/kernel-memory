using Microsoft.KernelMemory.GraphRAG.Configuration;
// ReSharper disable once CheckNamespace
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory;

/// <summary>
///     Extensions for KernelMemoryBuilder
/// </summary>
public static partial class KernelMemoryBuilderExtensions
{
    /// <summary>
    ///     Kernel Memory Builder extension method to add the GraphRAG connector.
    /// </summary>
    /// <param name="builder">The IKernelMemoryBuilder instance.</param>
    /// <param name="configuration">The GraphRAG configuration.</param>
    public static IKernelMemoryBuilder WithGraphRag(
        this IKernelMemoryBuilder builder,
        GraphRagConfig configuration)
    {
        builder.Services.AddGraphRag(configuration);

        return builder;
    }


    /// <summary>
    ///     Kernel Memory Builder extension method to add GraphRAG with default configuration.
    /// </summary>
    /// <param name="builder">The IKernelMemoryBuilder instance.</param>
    public static IKernelMemoryBuilder WithGraphRag(this IKernelMemoryBuilder builder)
    {
        builder.Services.AddGraphRagWithDefaults();

        return builder;
    }


    /// <summary>
    ///     Kernel Memory Builder extension method to add GraphRAG with custom configuration.
    /// </summary>
    /// <param name="builder">The IKernelMemoryBuilder instance.</param>
    /// <param name="configureOptions">Action to configure GraphRAG options.</param>
    public static IKernelMemoryBuilder WithGraphRag(
        this IKernelMemoryBuilder builder,
        Action<GraphRagConfig> configureOptions)
    {
        builder.Services.AddGraphRag(configureOptions);

        return builder;
    }
}
