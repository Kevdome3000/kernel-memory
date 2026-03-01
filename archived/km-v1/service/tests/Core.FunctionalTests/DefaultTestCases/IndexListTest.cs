// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KM.Core.FunctionalTests.DefaultTestCases;

public static class IndexListTest
{
    private static string NormalizeName(string raw, string sep)
    {
        return raw.Replace("-", sep).Replace("_", sep);
    }


    public static async Task ItNormalizesIndexNames(IKernelMemory memory, Action<string> log, string separator = "-")
    {
        // Arrange
        const string indexNameWithDashes = "name-with-dashes";
        const string indexNameWithUnderscores = "name_with_underscore";

        // Act - Assert no exception occurs
        await memory.ImportTextAsync("something", index: indexNameWithDashes);
        await memory.ImportTextAsync("something", index: indexNameWithUnderscores);

        // Cleanup
        await memory.DeleteIndexAsync(indexNameWithDashes);
        await memory.DeleteIndexAsync(indexNameWithUnderscores);
    }


    public static async Task ItUsesDefaultIndexName(IKernelMemory memory, Action<string> log, string expectedDefault)
    {
        // Arrange
        string emptyIndexName = string.Empty;

        // Act
        var id = await memory.ImportTextAsync("something", index: emptyIndexName);
        var count = 0;

        while (!await memory.IsDocumentReadyAsync(id))
        {
            Assert.True(count++ <= 30, "Document import timed out");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var list = (await memory.ListIndexesAsync()).ToList();

        // Clean up before exceptions can occur
        await memory.DeleteIndexAsync(emptyIndexName);

        // Assert
        Assert.True(list.Any(x => x.Name == expectedDefault));
    }


    public static async Task ItListsIndexes(IKernelMemory memory, Action<string> log, string separator = "-")
    {
        // Arrange
        string indexName1 = Guid.NewGuid().ToString("D");
        string indexName2 = Guid.NewGuid().ToString("D");
        string indexNameWithDashes = "name-with-dashes";
        string indexNameWithUnderscores = "name_with_underscore";

        Console.WriteLine("Index 1:" + indexName1);
        Console.WriteLine("Index 2:" + indexName2);
        Console.WriteLine("Index 3:" + indexNameWithDashes);
        Console.WriteLine("Index 4:" + indexNameWithUnderscores);

        string id1 = await memory.ImportTextAsync("text1", index: indexName1, steps: Constants.PipelineWithoutSummary);
        string id2 = await memory.ImportTextAsync("text2", index: indexName2, steps: Constants.PipelineWithoutSummary);
        string id3 = await memory.ImportTextAsync("text3", index: indexNameWithDashes, steps: Constants.PipelineWithoutSummary);
        string id4 = await memory.ImportTextAsync("text4", index: indexNameWithUnderscores, steps: Constants.PipelineWithoutSummary);

        while (!await memory.IsDocumentReadyAsync(id1, indexName1))
        {
            log($"[id1: {id1}] Waiting for memory ingestion to complete...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        while (!await memory.IsDocumentReadyAsync(id2, indexName2))
        {
            log($"[id2: {id2}] Waiting for memory ingestion to complete...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        while (!await memory.IsDocumentReadyAsync(id3, indexNameWithDashes))
        {
            log($"[id3: {id3}] Waiting for memory ingestion to complete...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        while (!await memory.IsDocumentReadyAsync(id4, indexNameWithUnderscores))
        {
            log($"[id4: {id4}] Waiting for memory ingestion to complete...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // Act
        List<IndexDetails> list = (await memory.ListIndexesAsync()).ToList();
        Console.WriteLine("Indexes found:");

        foreach (var index in list)
        {
            Console.WriteLine(" - " + index.Name);
        }

        // Clean up before exceptions can occur
        await memory.DeleteIndexAsync(indexName1);
        await memory.DeleteIndexAsync(indexName2);
        await memory.DeleteIndexAsync(indexNameWithDashes);
        await memory.DeleteIndexAsync(indexNameWithUnderscores);

        // Expected names per backend
        string expected1 = NormalizeName(indexName1, separator);
        string expected2 = NormalizeName(indexName2, separator);
        string expectedDashes = NormalizeName(indexNameWithDashes, separator);
        string expectedUnderscores = NormalizeName(indexNameWithUnderscores, separator);

        var names = list.Select(x => x.Name).ToList();

        // Assert
        Assert.Contains(expected1, names);
        Assert.Contains(expected2, names);
        Assert.Contains(expectedDashes, names);
        Assert.Contains(expectedUnderscores, names);

        // Only enforce the "no raw underscore variant" rule when backend uses hyphens
        if (separator == "-")
        {
            Assert.DoesNotContain(indexNameWithUnderscores, names);
        }
    }
}
