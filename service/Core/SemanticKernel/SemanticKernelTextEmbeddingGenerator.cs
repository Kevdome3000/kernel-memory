// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.SemanticKernel.Embeddings;

namespace Microsoft.KernelMemory.SemanticKernel;

public sealed class SemanticKernelTextEmbeddingGenerator : ITextEmbeddingGenerator
{
    private readonly ITextEmbeddingGenerationService _service;
    private readonly ITextTokenizer _tokenizer;
    private readonly ILogger<SemanticKernelTextEmbeddingGenerator> _log;

    /// <inheritdoc />
    public int MaxTokens { get; }


    /// <inheritdoc />
    public int CountTokens(string text)
    {
        return _tokenizer.CountTokens(text);
    }


    /// <inheritdoc />
    public IReadOnlyList<string> GetTokens(string text)
    {
        return _tokenizer.GetTokens(text);
    }


    public SemanticKernelTextEmbeddingGenerator(
        ITextEmbeddingGenerationService textEmbeddingGenerationService,
        SemanticKernelConfig config,
        ITextTokenizer? textTokenizer = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullExceptionEx.ThrowIfNull(textEmbeddingGenerationService, nameof(textEmbeddingGenerationService), "Embedding generation service is null");

        _service = textEmbeddingGenerationService;
        MaxTokens = config.MaxTokenTotal;

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<SemanticKernelTextEmbeddingGenerator>();

        if (textTokenizer == null)
        {
            textTokenizer = new CL100KTokenizer();
            _log.LogWarning(
                "Tokenizer not specified, will use {0}. The token count might be incorrect, causing unexpected errors",
                textTokenizer.GetType().FullName);
        }

        _tokenizer = textTokenizer;
    }


    /// <inheritdoc />
    public Task<Embedding> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Generating embedding with SK embedding generator service");

        return _service.GenerateEmbeddingAsync(text, cancellationToken);
    }
}
