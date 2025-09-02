// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;

namespace Microsoft.KM.Core.FunctionalTests.DefaultTestCases;

public static class IndexDeletionTest
{
    public static async Task ItDeletesIndexes(IKernelMemory memory, Action<string> log)
    {
        // Act
        await memory.ImportTextAsync(
            "this is a test",
            "text1",
            index: "index1",
            steps: ["extract", "partition", "gen_embeddings", "save_records"]);

        await memory.ImportTextAsync(
            "this is a test",
            "text2",
            index: "index1",
            steps: ["extract", "partition", "gen_embeddings", "save_records"]);

        await memory.ImportTextAsync(
            "this is a test",
            "text3",
            index: "index2",
            steps: ["extract", "partition", "gen_embeddings", "save_records"]);

        await memory.ImportTextAsync(
            "this is a test",
            "text4",
            index: "index2",
            steps: ["extract", "partition", "gen_embeddings", "save_records"]);

        // Assert (no exception occurs, manual verification of collection being deleted)
        await memory.DeleteDocumentAsync("text1", "index1");
        await memory.DeleteIndexAsync("index2");
    }
}
