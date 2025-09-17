using Microsoft.KernelMemory.StructRAG;

// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory;

public static class KernelMemoryBuilderExtension
{
    public static IKernelMemoryBuilder WithStructRagSearchClient(this IKernelMemoryBuilder builder)
    {
        return builder.WithCustomSearchClient<StructRAGSearchClient>();
    }
}
