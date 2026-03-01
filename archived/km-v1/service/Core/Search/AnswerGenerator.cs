// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Prompts;

namespace Microsoft.KernelMemory.Search;

[Experimental("KMEXP05")]
internal class AnswerGenerator
{
    private readonly ILogger<AnswerGenerator> _log;
    private readonly IContentModeration? _contentModeration;
    private readonly SearchClientConfig _config;
    private readonly string _answerPrompt;
    private readonly ITextGenerator _textGenerator;


    public AnswerGenerator(
        ITextGenerator textGenerator,
        SearchClientConfig? config = null,
        IPromptProvider? promptProvider = null,
        IContentModeration? contentModeration = null,
        ILoggerFactory? loggerFactory = null)
    {
        _textGenerator = textGenerator;
        _contentModeration = contentModeration;
        _config = config ?? new SearchClientConfig();
        _config.Validate();
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<AnswerGenerator>();

        promptProvider ??= new EmbeddedPromptProvider();
        _answerPrompt = promptProvider.ReadPrompt(Constants.PromptNamesAnswerWithFacts);

        if (_textGenerator == null)
        {
            throw new KernelMemoryException("Text generator not configured");
        }

        if (_contentModeration == null || !_config.UseContentModeration)
        {
            _log.LogInformation("Content moderation is not enabled.");
        }
    }


    internal async IAsyncEnumerable<MemoryAnswer> GenerateAnswerAsync(
        string question,
        SearchClientResult result,
        IContext? context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prompt = PreparePrompt(question, result.Facts.ToString(), context);
        var promptSize = _textGenerator.CountTokens(prompt);
        _log.LogInformation("RAG prompt ({0} tokens): {1}", promptSize, prompt);

        var tokenUsage = new TokenUsage
        {
            Timestamp = DateTimeOffset.UtcNow,
            ModelType = Constants.ModelType.TextGeneration,
            TokenizerTokensIn = promptSize
        };
        result.AddTokenUsageToStaticResults(tokenUsage);

        if (result.FactsAvailableCount > 0 && result.FactsUsedCount == 0)
        {
            _log.LogError("Unable to inject memories in the prompt, not enough tokens available");
            yield return result.InsufficientTokensResult;

            yield break;
        }

        if (result.FactsUsedCount == 0)
        {
            _log.LogWarning("No memories available");
            yield return result.NoFactsResult;

            yield break;
        }

        var completeAnswerTokens = new StringBuilder();

        await foreach (GeneratedTextContent answerToken in GenerateAnswerTokensAsync(prompt, context, cancellationToken).ConfigureAwait(false))
        {
            completeAnswerTokens.Append(answerToken.Text);
            tokenUsage.Merge(answerToken.TokenUsage);
            result.AskResult.Result = answerToken.Text;

            yield return result.AskResult;
        }

        // Check if the complete answer is empty
        string completeAnswer = completeAnswerTokens.ToString();

        if (string.IsNullOrWhiteSpace(completeAnswer) || ValueIsEquivalentTo(completeAnswer, _config.EmptyAnswer))
        {
            _log.LogInformation("No relevant memories found, returning empty answer.");
            yield return result.NoFactsResult;

            yield break;
        }

        _log.LogSensitive("Answer: {0}", completeAnswer);

        // Check if the complete answer is safe
        if (_config.UseContentModeration
            && _contentModeration != null
            && !await _contentModeration.IsSafeAsync(completeAnswer, cancellationToken).ConfigureAwait(false))
        {
            _log.LogWarning("Unsafe answer detected. Returning error message instead.");
            yield return result.UnsafeAnswerResult;

            yield break;
        }

        // Add token usage report at the end
        result.AskResult.Result = string.Empty;
        tokenUsage.TokenizerTokensOut = _textGenerator.CountTokens(completeAnswer);
        result.AskResult.TokenUsage = [tokenUsage];
        yield return result.AskResult;
    }


    private string PreparePrompt(string question, string facts, IContext? context)
    {
        string prompt = context.GetCustomRagPromptOrDefault(_answerPrompt);
        string emptyAnswer = context.GetCustomEmptyAnswerTextOrDefault(_config.EmptyAnswer);

        question = question.Trim();
        question = question.EndsWith('?')
            ? question
            : $"{question}?";

        prompt = prompt.Replace("{{$facts}}", facts.Trim(), StringComparison.OrdinalIgnoreCase);
        prompt = prompt.Replace("{{$input}}", question, StringComparison.OrdinalIgnoreCase);
        prompt = prompt.Replace("{{$notFound}}", emptyAnswer, StringComparison.OrdinalIgnoreCase);

        return prompt;
    }


    private IAsyncEnumerable<GeneratedTextContent> GenerateAnswerTokensAsync(string prompt, IContext? context, CancellationToken cancellationToken)
    {
        int maxTokens = context.GetCustomRagMaxTokensOrDefault(_config.AnswerTokens);
        double temperature = context.GetCustomRagTemperatureOrDefault(_config.Temperature);
        double nucleusSampling = context.GetCustomRagNucleusSamplingOrDefault(_config.TopP);

        var options = new TextGenerationOptions
        {
            MaxTokens = maxTokens,
            Temperature = temperature,
            NucleusSampling = nucleusSampling,
            PresencePenalty = _config.PresencePenalty,
            FrequencyPenalty = _config.FrequencyPenalty,
            StopSequences = _config.StopSequences,
            TokenSelectionBiases = _config.TokenSelectionBiases
        };

        if (_log.IsEnabled(LogLevel.Debug))
        {
            _log.LogDebug("Running RAG prompt, size: {0} tokens, requesting max {1} tokens",
                _textGenerator.CountTokens(prompt),
                _config.AnswerTokens);

            _log.LogSensitive("Prompt: {0}", prompt);
        }

        return _textGenerator.GenerateTextAsync(prompt, options, cancellationToken);
    }


    private static bool ValueIsEquivalentTo(string value, string target)
    {
        value = value.Trim()
            .Trim('.',
                '"',
                '\'',
                '`',
                '~',
                '!',
                '?',
                '@',
                '#',
                '$',
                '%',
                '^',
                '+',
                '*',
                '_',
                '-',
                '=',
                '|',
                '\\',
                '/',
                '(',
                ')',
                '[',
                ']',
                '{',
                '}',
                '<',
                '>');
        target = target.Trim()
            .Trim('.',
                '"',
                '\'',
                '`',
                '~',
                '!',
                '?',
                '@',
                '#',
                '$',
                '%',
                '^',
                '+',
                '*',
                '_',
                '-',
                '=',
                '|',
                '\\',
                '/',
                '(',
                ')',
                '[',
                ']',
                '{',
                '}',
                '<',
                '>');
        return string.Equals(value, target, StringComparison.OrdinalIgnoreCase);
    }
}
