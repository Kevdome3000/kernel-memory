// Copyright (c) Microsoft.All rights reserved.

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.KM.Core.FunctionalTests;

public class ParallelHandlersTest : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;


    public ParallelHandlersTest(
        IConfiguration cfg,
        ITestOutputHelper output) : base(cfg, output)
    {
        _memory = new KernelMemoryBuilder()
            .WithOpenAI(OpenAiConfig)
            // Store data in memory
            .WithSimpleFileStorage(SimpleFileStorageConfig.Persistent)
            .WithSimpleVectorDb(SimpleVectorDbConfig.Persistent)
            .Build<MemoryServerless>();
    }


    [Fact]
    [Trait("Category", "Serverless")]
    public async Task ItUsesParallelEmbeddingGeneration()
    {
        // Arrange
        const string Id = "ItUploadsPDFDocsAndDeletes-file2-largePDF.pdf";
        Console.WriteLine("Uploading document");
        var clock = new Stopwatch();
        clock.Start();
        await _memory.ImportDocumentAsync(
            "file2-largePDF.pdf",
            Id,
            steps:
            [
                Constants.PipelineStepsExtract,
                Constants.PipelineStepsPartition,
                "gen_embeddings_parallel", // alternative to default "gen_embeddings", 3 secs vs 12 secs
                Constants.PipelineStepsSaveRecords
            ]);

        var count = 0;

        while (!await _memory.IsDocumentReadyAsync(Id))
        {
            Assert.True(count++ <= 30, "Document import timed out");
            Console.WriteLine("Waiting for memory ingestion to complete...");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        clock.Stop();

        // Act
        Console.WriteLine($"Time taken: {clock.ElapsedMilliseconds} msecs");
        var answer = await _memory.AskAsync("What's the purpose of the planning system?");
        Console.WriteLine(answer.Result);

        // Assert
        Assert.Contains("sustainable development", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        Console.WriteLine("Deleting memories extracted from the document");
        await _memory.DeleteDocumentAsync(Id);
    }


    [Fact]
    [Trait("Category", "Serverless")]
    public async Task ItUsesParallelSummarization()
    {
        // Arrange
        const string Id = "ItUploadsPDFDocsAndDeletes-file2-largePDF.pdf";
        Console.WriteLine("Uploading document");
        var clock = new Stopwatch();
        clock.Start();
        await _memory.ImportDocumentAsync(
            "file2-largePDF.pdf",
            Id,
            steps:
            [
                Constants.PipelineStepsExtract,
                "summarize", // alternative to default "summarize", 55secs vs 50secs
                Constants.PipelineStepsGenEmbeddings,
                Constants.PipelineStepsSaveRecords
            ]);

        var count = 0;

        while (!await _memory.IsDocumentReadyAsync(Id))
        {
            Assert.True(count++ <= 230, "Document import timed out");
            Console.WriteLine("Waiting for summarization to complete...");
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        clock.Stop();

        // Act
        Console.WriteLine($"Time taken: {clock.ElapsedMilliseconds} msecs");
        var results = await _memory.SearchSyntheticsAsync("summary", filter: MemoryFilters.ByDocument(Id));

        foreach (Citation result in results)
        {
            Console.WriteLine($"== {result.SourceName} summary ==\n{result.Partitions.First().Text}\n");
        }

        // Cleanup
        Console.WriteLine("Deleting memories extracted from the document");
        await _memory.DeleteDocumentAsync(Id);
    }
}
