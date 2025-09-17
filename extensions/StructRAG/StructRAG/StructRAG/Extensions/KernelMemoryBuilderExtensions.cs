
using Microsoft.Extensions.DependencyInjection;
// ReSharper disable once CheckNamespace
using Microsoft.KernelMemory.StructRAG;

// ReSharper disable InconsistentNaming

// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory;

/// <summary>
///     Extensions for KernelMemoryBuilder
/// </summary>
public static class KernelMemoryBuilderExtensions
{
    /// <summary>
    ///     Kernel Memory Builder extension method to add the StructRAG search client.
    /// </summary>
    /// <param name="builder">The IKernelMemoryBuilder instance.</param>
    /// <returns>The IKernelMemoryBuilder instance for method chaining.</returns>
    public static IKernelMemoryBuilder WithStructRAGSearchClient(this IKernelMemoryBuilder builder)
    {
        return builder.WithCustomSearchClient<StructRAGSearchClient>();
    }


    /// <summary>
    ///     Kernel Memory Builder extension method to add the StructRAG search client with configuration.
    /// </summary>
    /// <param name="builder">The IKernelMemoryBuilder instance.</param>
    /// <param name="config">The SearchClientConfig configuration.</param>
    /// <returns>The IKernelMemoryBuilder instance for method chaining.</returns>
    public static IKernelMemoryBuilder WithStructRAGSearchClient(
        this IKernelMemoryBuilder builder,
        SearchClientConfig config)
    {
        builder.Services.AddSingleton(config);
        return builder.WithCustomSearchClient<StructRAGSearchClient>();
    }
}
