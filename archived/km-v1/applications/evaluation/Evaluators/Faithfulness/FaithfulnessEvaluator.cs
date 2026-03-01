// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.KernelMemory.Evaluation;
using Microsoft.KernelMemory.Evaluators.AnswerCorrectness;
using Microsoft.KernelMemory.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

#pragma warning disable IDE0130 // reduce number of "using" statements
// ReSharper disable CheckNamespace
namespace Microsoft.KernelMemory.Evaluators.Faithfulness;

internal sealed class FaithfulnessEvaluator : EvaluationEngine
{
    private readonly Kernel _kernel;

    private KernelFunction ExtractStatements => _kernel.CreateFunctionFromPrompt(GetSKPrompt("Extraction", "Statements"),
        new OpenAIPromptExecutionSettings
        {
            Temperature = 1e-8f,
            Seed = 0,
            ResponseFormat = "json_object"
        },
        nameof(ExtractStatements));

    private KernelFunction FaithfulnessEvaluation => _kernel.CreateFunctionFromPrompt(GetSKPrompt("Evaluation", "Faithfulness"),
        new OpenAIPromptExecutionSettings
        {
            Temperature = 1e-8f,
            Seed = 0,
            ResponseFormat = "json_object"
        },
        nameof(FaithfulnessEvaluation));


    public FaithfulnessEvaluator(Kernel kernel)
    {
        _kernel = kernel.Clone();
    }


    internal async Task<float> Evaluate(MemoryAnswer answer, Dictionary<string, object?> metadata)
    {
        var extraction = await Try(3,
                async remainingTry =>
                {
                    var extraction = await ExtractStatements.InvokeAsync(_kernel,
                            new KernelArguments
                            {
                                { "question", answer.Question },
                                { "answer", answer.Result }
                            })
                        .ConfigureAwait(false);

                    return JsonSerializer.Deserialize<StatementExtraction>(extraction.GetValue<string>()!);
                })
            .ConfigureAwait(false);

        if (extraction is null)
        {
            return 0;
        }

        var faithfulness = await Try(3,
                async remainingTry =>
                {
                    var evaluation = await FaithfulnessEvaluation.InvokeAsync(_kernel,
                            new KernelArguments
                            {
                                { "context", string.Join('\n', answer.RelevantSources.SelectMany(c => c.Partitions.Select(p => p.Text))) },
                                { "answer", answer.Result },
                                { "statements", JsonSerializer.Serialize(extraction) }
                            })
                        .ConfigureAwait(false);

                    var faithfulness = JsonSerializer.Deserialize<FaithfulnessEvaluations>(evaluation.GetValue<string>()!);

                    return faithfulness;
                })
            .ConfigureAwait(false);

        if (faithfulness is null)
        {
            return 0;
        }

        metadata.Add($"{nameof(FaithfulnessEvaluator)}-Evaluation", faithfulness);

        return faithfulness.Evaluations.Count(c => c.Verdict > 0) / (float)extraction.Statements.Count;
    }
}
