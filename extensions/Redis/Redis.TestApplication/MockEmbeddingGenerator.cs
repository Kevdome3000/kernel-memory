// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;

namespace Microsoft.Redis.TestApplication;

internal sealed class MockEmbeddingGenerator : ITextEmbeddingGenerator
{
    private readonly Dictionary<string, Embedding> _embeddings = [];


    internal void AddFakeEmbedding(string str, Embedding vector)
    {
        _embeddings.Add(str, vector);
    }


    /// <inheritdoc />
    public int CountTokens(string text)
    {
        return 0;
    }


    /// <inheritdoc />
    public IReadOnlyList<string> GetTokens(string text)
    {
        return [];
    }


    /// <inheritdoc />
    public int MaxTokens => 0;


    /// <inheritdoc />
    public Task<Embedding> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_embeddings[text]);
    }
}
