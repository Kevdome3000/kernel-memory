// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KM.Core.FunctionalTests.DefaultTestCases;

public static class MissingIndexTest
{
    private const string NotFound = "INFO NOT FOUND";


    private static string NormalizeName(string raw, string sep)
    {
        return raw.Replace("-", sep).Replace("_", sep);
    }


    public static async Task ItHandlesMissingIndexesConsistently(IKernelMemory memory, Action<string> log, string separator = "-")
    {
        // Arrange
        string indexName = Guid.NewGuid().ToString("D");
        await memory.DeleteIndexAsync(indexName);

        // Act: verify the index doesn't exist
        string expectedName = NormalizeName(indexName, separator);
        IEnumerable<IndexDetails> list = await memory.ListIndexesAsync();
        Assert.False(list.Any(x => x.Name == expectedName));

        // Act: Delete a non-existing index, no exception
        await memory.DeleteIndexAsync(indexName);

        // Act: Search a non-existing index
        var answer = await memory.AskAsync("What's the number after 9?", indexName);
        Assert.Equal(NotFound, answer.Result);

        // Act: Search a non-existing index
        var searchResult = await memory.SearchAsync("some query", indexName);
        Assert.Equal(0, searchResult.Results.Count);

        // Act: delete doc from non existing index
        await memory.DeleteDocumentAsync(Guid.NewGuid().ToString("D"), indexName);

        // Act: get status of non existing doc/index
        bool isReady = await memory.IsDocumentReadyAsync(Guid.NewGuid().ToString("D"), indexName);
        Assert.Equal(false, isReady);

        // Act: get status of non existing doc/index
        DataPipelineStatus? status = await memory.GetDocumentStatusAsync(Guid.NewGuid().ToString("D"), indexName);
        Assert.Null(status);

        // Assert: verify the index doesn't exist yet
        list = await memory.ListIndexesAsync();
        Assert.False(list.Any(x => x.Name == expectedName));

        // Act: import into a non existing index - the index is created
        var id = await memory.ImportTextAsync("some text", "foo", index: indexName);
        Assert.NotEmpty(id);
        isReady = false;
        var attempts = 10;

        while (!isReady && attempts-- > 0)
        {
            isReady = await memory.IsDocumentReadyAsync(id);

            if (!isReady) { await Task.Delay(TimeSpan.FromMilliseconds(500)); }
        }

        // Assert: verify the index has been created
        list = await memory.ListIndexesAsync();
        Assert.True(list.Any(x => x.Name == expectedName));

        // clean up
        await memory.DeleteIndexAsync(indexName);
    }
}
