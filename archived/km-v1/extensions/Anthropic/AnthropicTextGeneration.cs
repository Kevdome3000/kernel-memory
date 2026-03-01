// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI.Anthropic.Client;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.AI.Anthropic;

/// <summary>
/// Anthropic LLMs text generation client
/// </summary>
public sealed class AnthropicTextGeneration : ITextGenerator, IDisposable
{
    private const string DefaultEndpoint = "https://api.anthropic.com";
    private const string DefaultEndpointVersion = "2023-06-01";
    private const string DefaultSystemPrompt = "You are an helpful assistant.";

    private readonly RawAnthropicClient _client;
    private readonly ITextTokenizer _textTokenizer;
    private readonly IContextProvider _contextProvider;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicTextGeneration> _log;
    private readonly string _modelName;
    private readonly string _defaultSystemPrompt;


    /// <summary>
    /// Create new instance of Anthropic client
    /// </summary>
    /// <param name="config">Client configuration, including credentials and model details</param>
    /// <param name="textTokenizer">Tokenizer used to count tokens</param>
    /// <param name="httpClientFactory">Optional factory used to inject a pre-configured HTTP client for requests to Anthropic API</param>
    /// <param name="contextProvider">Request context provider with runtime configuration overrides</param>
    /// <param name="loggerFactory">Optional factory used to inject configured loggers</param>
    public AnthropicTextGeneration(
        AnthropicConfig config,
        ITextTokenizer? textTokenizer = null,
        IHttpClientFactory? httpClientFactory = null,
        IContextProvider? contextProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        _modelName = config.TextModelName;
        _defaultSystemPrompt = !string.IsNullOrWhiteSpace(config.DefaultSystemPrompt)
            ? config.DefaultSystemPrompt
            : DefaultSystemPrompt;

        // Using the smallest value for now - KM support MaxTokenIn and MaxTokenOut TODO
        MaxTokenTotal = config.MaxTokenOut;

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<AnthropicTextGeneration>();
        _contextProvider = contextProvider ?? new RequestContextProvider();

        if (httpClientFactory == null)
        {
            _httpClient = new HttpClient();
        }
        else
        {
            _httpClient = string.IsNullOrWhiteSpace(config.HttpClientName)
                ? httpClientFactory.CreateClient()
                : httpClientFactory.CreateClient(config.HttpClientName);
        }

        var endpoint = string.IsNullOrWhiteSpace(config.Endpoint)
            ? DefaultEndpoint
            : config.Endpoint;
        var endpointVersion = string.IsNullOrWhiteSpace(config.Endpoint)
            ? DefaultEndpointVersion
            : config.EndpointVersion;
        _client = new RawAnthropicClient(_httpClient,
            endpoint,
            endpointVersion,
            config.ApiKey);

        textTokenizer ??= TokenizerFactory.GetTokenizerForEncoding(config.Tokenizer);

        if (textTokenizer == null)
        {
            textTokenizer = new CL100KTokenizer();
            _log.LogWarning(
                "Tokenizer not specified, will use {0}. The token count might be incorrect, causing unexpected errors",
                textTokenizer.GetType().FullName);
        }

        _textTokenizer = textTokenizer;
    }


    /// <inheritdoc />
    public int MaxTokenTotal { get; private set; }


    /// <inheritdoc />
    public int CountTokens(string text)
    {
        return _textTokenizer.CountTokens(text);
    }


    /// <inheritdoc/>
    public IReadOnlyList<string> GetTokens(string text)
    {
        return _textTokenizer.GetTokens(text);
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<GeneratedTextContent> GenerateTextAsync(
        string prompt,
        TextGenerationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string modelName = _contextProvider.GetContext().GetCustomTextGenerationModelNameOrDefault(_modelName);

        _log.LogTrace("Sending text generation request, model '{0}'", modelName);

        CallClaudeStreamingParams parameters = new(modelName, prompt)
        {
            System = _defaultSystemPrompt,
            Temperature = options.Temperature
        };

        IAsyncEnumerable<StreamingResponseMessage> streamedResponse = _client.CallClaudeStreamingAsync(parameters, cancellationToken);

        await foreach (StreamingResponseMessage response in streamedResponse.ConfigureAwait(false))
        {
            //now we simply yield the response
            switch (response)
            {
                case ContentBlockDelta blockDelta:
                    yield return blockDelta.Delta.Text;

                    break;

            }
        }
    }


    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
