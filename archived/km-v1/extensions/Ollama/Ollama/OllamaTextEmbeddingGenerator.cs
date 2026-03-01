// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.Diagnostics;
using OllamaSharp;
using OllamaSharp.Models;

namespace Microsoft.KernelMemory.AI.Ollama;

public class OllamaTextEmbeddingGenerator : ITextEmbeddingGenerator, ITextEmbeddingBatchGenerator
{
    private const int MaxTokensIfUndefined = 8192;

    private readonly IOllamaApiClient _client;
    private readonly OllamaModelConfig _modelConfig;
    private readonly ITextTokenizer _textTokenizer;
    private readonly IContextProvider _contextProvider;
    private readonly ILogger<OllamaTextEmbeddingGenerator> _log;

    public int MaxTokens { get; }

    public int MaxBatchSize { get; }


    public OllamaTextEmbeddingGenerator(
        IOllamaApiClient ollamaClient,
        OllamaModelConfig modelConfig,
        ITextTokenizer? textTokenizer = null,
        IContextProvider? contextProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        _client = ollamaClient;
        _modelConfig = modelConfig;
        MaxBatchSize = modelConfig.MaxBatchSize;
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<OllamaTextEmbeddingGenerator>();

        textTokenizer ??= TokenizerFactory.GetTokenizerForEncoding(modelConfig.Tokenizer);

        if (textTokenizer == null)
        {
            textTokenizer = new CL100KTokenizer();
            _log.LogWarning(
                "Tokenizer not specified, will use {0}. The token count might be incorrect, causing unexpected errors",
                textTokenizer.GetType().FullName);
        }

        _textTokenizer = textTokenizer;
        _contextProvider = contextProvider ?? new RequestContextProvider();

        MaxTokens = modelConfig.MaxTokenTotal ?? MaxTokensIfUndefined;
    }


    public OllamaTextEmbeddingGenerator(
        OllamaConfig config,
        ITextTokenizer? textTokenizer = null,
        IContextProvider? contextProvider = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            new OllamaApiClient(new Uri(config.Endpoint), config.EmbeddingModel.ModelName),
            config.EmbeddingModel,
            textTokenizer,
            contextProvider,
            loggerFactory)
    {
    }


    public OllamaTextEmbeddingGenerator(
        HttpClient httpClient,
        OllamaConfig config,
        ITextTokenizer? textTokenizer = null,
        IContextProvider? contextProvider = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            new OllamaApiClient(httpClient, config.EmbeddingModel.ModelName),
            config.EmbeddingModel,
            textTokenizer,
            contextProvider,
            loggerFactory)
    {
    }


    public int CountTokens(string text)
    {
        return _textTokenizer.CountTokens(text);
    }


    public IReadOnlyList<string> GetTokens(string text)
    {
        return _textTokenizer.GetTokens(text);
    }


    public async Task<Embedding> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Generating embedding, text length {0} chars", text.Length);

        Embedding[] result = await GenerateEmbeddingBatchAsync([text], cancellationToken).ConfigureAwait(false);
        var embeddding = result.First();
        _log.LogTrace("Embedding ready, vector length {0}", embeddding.Length);

        return embeddding;
    }


    public async Task<Embedding[]> GenerateEmbeddingBatchAsync(
        IEnumerable<string> textList,
        CancellationToken cancellationToken = default)
    {
        var list = textList.ToList();

        string modelName = _contextProvider.GetContext().GetCustomEmbeddingGenerationModelNameOrDefault(_client.SelectedModel);
        _log.LogTrace("Generating embeddings batch, size {0} texts, with model {1}", list.Count, modelName);

        var request = new EmbedRequest
        {
            Model = modelName,
            Input = list,
            Options = new RequestOptions
            {
                // Global settings
                MiroStat = _modelConfig.MiroStat,
                MiroStatEta = _modelConfig.MiroStatEta,
                MiroStatTau = _modelConfig.MiroStatTau,
                NumCtx = _modelConfig.NumCtx,
                NumGqa = _modelConfig.NumGqa,
                NumGpu = _modelConfig.NumGpu,
                NumThread = _modelConfig.NumThread,
                RepeatLastN = _modelConfig.RepeatLastN,
                Seed = _modelConfig.Seed,
                TfsZ = _modelConfig.TfsZ,
                NumPredict = _modelConfig.NumPredict,
                TopK = _modelConfig.TopK,
                MinP = _modelConfig.MinP
            }
        };

        EmbedResponse response = await _client.EmbedAsync(request, cancellationToken).ConfigureAwait(false);
        Embedding[] result = response.Embeddings.Select(x => new Embedding(x)).ToArray();

        _log.LogTrace("Embeddings batch ready, size {0} texts", result.Length);

        return result;
    }
}
