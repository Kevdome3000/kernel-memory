// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Evaluation;
using Microsoft.KernelMemory.Evaluation.TestSet;
using Microsoft.SemanticKernel;

namespace Microsoft.KM.Evaluation.FunctionalTests;

#pragma warning disable SKEXP0010
public class TestsetGenerationTests
{
    private readonly IKernelMemory _memory;
    private readonly TestSetGenerator _testSetGenerator;
    private readonly TestSetEvaluator _testSetEvaluator;
    private readonly Kernel _kernel;


    public TestsetGenerationTests(IConfiguration cfg, ITestOutputHelper output)
    {
        var azureOpenAITextConfig = new AzureOpenAIConfig();
        var azureOpenAIEmbeddingConfig = new AzureOpenAIConfig();

        cfg
            .BindSection("KernelMemory:Services:AzureOpenAIText", azureOpenAITextConfig)
            .BindSection("KernelMemory:Services:AzureOpenAIEmbedding", azureOpenAIEmbeddingConfig);

        var memoryBuilder = new KernelMemoryBuilder()
            .With(new KernelMemoryConfig { DefaultIndexName = "default4tests" })
            .WithAzureOpenAITextGeneration(azureOpenAITextConfig)
            .WithAzureOpenAITextEmbeddingGeneration(azureOpenAIEmbeddingConfig);

        _kernel = Kernel
            .CreateBuilder()
            .AddAzureOpenAITextEmbeddingGeneration(
                azureOpenAIEmbeddingConfig.Deployment,
                azureOpenAIEmbeddingConfig.Endpoint,
                azureOpenAIEmbeddingConfig.APIKey)
            .AddAzureOpenAIChatCompletion(
                azureOpenAITextConfig.Deployment,
                azureOpenAITextConfig.Endpoint,
                azureOpenAITextConfig.APIKey)
            .Build();

        _testSetGenerator = new TestSetGeneratorBuilder(memoryBuilder.Services)
            .AddEvaluatorKernel(_kernel)
            .Build();

        _memory = memoryBuilder.Build();

        _testSetEvaluator = new TestSetEvaluatorBuilder()
            .AddEvaluatorKernel(_kernel)
            .WithMemory(_memory)
            .Build();
    }


    [Fact]
    [Trait("Category", "Evaluation")]
    public async Task ItGenerateTestSetAsync()
    {
        await _memory
            .ImportDocumentAsync(
                "file1-NASA-news.pdf",
                "file1-NASA-news",
                steps: Constants.PipelineWithoutSummary)
            .ConfigureAwait(false);

        var testSets = await _testSetGenerator.GenerateTestSetsAsync("default4tests", retryCount: 5, count: 1)
            .ToArrayAsync()
            .ConfigureAwait(false);

        Assert.NotEmpty(testSets);
        Assert.Equal(1, testSets.Length);
    }


    [Fact]
    [Trait("Category", "Evaluation")]
    public async Task ItEvaluateTestSetAsync()
    {
        await _memory
            .ImportDocumentAsync(
                "file1-NASA-news.pdf",
                "file1-NASA-news",
                steps: Constants.PipelineWithoutSummary)
            .ConfigureAwait(false);

        var evaluation = await _testSetEvaluator.EvaluateTestSetAsync("default4tests",
            [
                new TestSetItem
                {
                    Question = "What is the role of the Department of Defense in the recovery operations for the Artemis II mission?",
                    GroundTruth = "The Department of Defense personnel are involved in practicing recovery operations for the Artemis II mission. They use a crew module test article to help verify the recovery team's readiness to recover the Artemis II crew and the Orion spacecraft."
                }
            ])
            .ToArrayAsync()
            .ConfigureAwait(false);

        Assert.NotEmpty(evaluation);
    }
}
