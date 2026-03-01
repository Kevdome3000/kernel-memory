// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.AI.LlamaSharp;

/// <summary>
/// Text generator based on LLama models, via LLamaSharp / llama.cpp
/// See https://github.com/SciSharp/LLamaSharp
/// </summary>
[Experimental("KMEXP01")]
public sealed class LlamaSharpTextGenerator : ITextGenerator, IDisposable
{
    private readonly LLamaWeights _model;
    private readonly LLamaContext _context;
    private readonly ITextTokenizer _textTokenizer;
    private readonly ILogger<LlamaSharpTextGenerator> _log;


    /// <summary>
    /// Create new instance
    /// </summary>
    /// <param name="config">Configuration settings</param>
    /// <param name="textTokenizer">Optional text tokenizer, replacing the one provided by the model</param>
    /// <param name="loggerFactory">Application logger instance</param>
    public LlamaSharpTextGenerator(
        LlamaSharpModelConfig config,
        ITextTokenizer? textTokenizer = null,
        ILoggerFactory? loggerFactory = null)
    {
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<LlamaSharpTextGenerator>();

        config.Validate();
        MaxTokenTotal = (int)config.MaxTokenTotal;

        var parameters = new ModelParams(config.ModelPath)
        {
            ContextSize = config.MaxTokenTotal,
            GpuLayerCount = config.GpuLayerCount ?? 20
        };

        var modelFilename = config.ModelPath.Split('/').Last().Split('\\').Last();
        _log.LogDebug("Loading LLama model: {1}", modelFilename);

        _model = LLamaWeights.LoadFromFile(parameters);
        _context = _model.CreateContext(parameters);
        _log.LogDebug("LLama model loaded");

        _textTokenizer = textTokenizer ?? new LlamaSharpTokenizer(_context);
    }


    /// <inheritdoc/>
    public int MaxTokenTotal { get; }


    /// <inheritdoc/>
    public int CountTokens(string text)
    {
        return _textTokenizer.CountTokens(text);
    }


    /// <inheritdoc/>
    public IReadOnlyList<string> GetTokens(string text)
    {
        return _textTokenizer.GetTokens(text);
    }


    /// <inheritdoc/>
    public IAsyncEnumerable<GeneratedTextContent> GenerateTextAsync(
        string prompt,
        TextGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        var executor = new InteractiveExecutor(_context);

        var logitBias = options.TokenSelectionBiases.Count > 0
            ? options.TokenSelectionBiases.ToDictionary(pair => (LLamaToken)pair.Key, pair => pair.Value)
            : [];

        var samplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = (float)options.Temperature,
            TopP = (float)options.NucleusSampling,
            PresencePenalty = (float)options.PresencePenalty,
            FrequencyPenalty = (float)options.FrequencyPenalty,
            LogitBias = logitBias
        };

        var settings = new InferenceParams
        {
            TokensKeep = MaxTokenTotal,
            MaxTokens = options.MaxTokens ?? -1,
            AntiPrompts = options.StopSequences?.ToList() ?? [],
            SamplingPipeline = samplingPipeline
        };

        _log.LogTrace("Generating text, temperature {0}, max tokens {1}", samplingPipeline.Temperature, settings.MaxTokens);
        return executor.InferAsync(prompt, settings, cancellationToken).Select(x => new GeneratedTextContent(x));
    }


    /// <inheritdoc/>
    public void Dispose()
    {
        _model.Dispose();
        _context.Dispose();
    }
}
