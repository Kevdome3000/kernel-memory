// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory.MemoryDb.Elasticsearch.Internals;

namespace Microsoft.Elasticsearch.FunctionalTests.Additional;

public class IndexManagementTests : MemoryDbFunctionalTest
{
    public IndexManagementTests(
        IConfiguration cfg,
        ITestOutputHelper output)
        : base(cfg, output)
    {
    }


    [Fact]
    [Trait("Category", "Elasticsearch")]
    public async Task CanCreateAndDeleteIndexAsync()
    {
        var indexName = nameof(CanCreateAndDeleteIndexAsync);
        var vectorSize = 1536;

        // Creates the index using IMemoryDb
        await MemoryDb.CreateIndexAsync(indexName, vectorSize)
            .ConfigureAwait(false);

        // Verifies the index is created using the ES client
        var actualIndexName = IndexNameHelper.Convert(nameof(CanCreateAndDeleteIndexAsync), ElasticsearchConfig);
        var resp = await Client.Indices.ExistsAsync(actualIndexName)
            .ConfigureAwait(false);
        Assert.True(resp.Exists);
        Output.WriteLine($"The index '{actualIndexName}' was created successfully.");

        // Deletes the index
        await MemoryDb.DeleteIndexAsync(indexName)
            .ConfigureAwait(false);

        // Verifies the index is deleted using the ES client
        resp = await Client.Indices.ExistsAsync(actualIndexName)
            .ConfigureAwait(false);
        Assert.False(resp.Exists);
        Output.WriteLine($"The index '{actualIndexName}' was deleted successfully.");
    }


    [Fact]
    [Trait("Category", "Elasticsearch")]
    public async Task CanGetIndicesAsync()
    {
        var indexNames = new[]
        {
            IndexNameHelper.Convert(nameof(CanGetIndicesAsync) + "-First", ElasticsearchConfig),
            IndexNameHelper.Convert(nameof(CanGetIndicesAsync) + "-Second", ElasticsearchConfig)
        };

        // Creates the indices using IMemoryDb
        foreach (var indexName in indexNames)
        {
            await MemoryDb.CreateIndexAsync(indexName, 1536)
                .ConfigureAwait(false);
        }

        // Verifies the indices are returned
        var indices = await MemoryDb.GetIndexesAsync()
            .ConfigureAwait(false);

        Assert.True(indices.All(nme => indices.Contains(nme)));

        // Cleans up
        foreach (var indexName in indexNames)
        {
            await MemoryDb.DeleteIndexAsync(indexName)
                .ConfigureAwait(false);
        }
    }
}
