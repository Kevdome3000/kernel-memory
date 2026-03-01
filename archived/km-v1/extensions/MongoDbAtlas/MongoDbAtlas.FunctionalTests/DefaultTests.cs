// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MongoDbAtlas.Internals;
using Microsoft.KM.Core.FunctionalTests.DefaultTestCases;
using Microsoft.KM.TestHelpers;

namespace Microsoft.MongoDbAtlas.FunctionalTests;

public class DefaultTestsSingleCollection : DefaultTests
{
    public DefaultTestsSingleCollection(IConfiguration cfg, ITestOutputHelper output)
        : base(cfg, output, false)
    {
    }
}


public class DefaultTestsMultipleCollections : DefaultTests
{
    public DefaultTestsMultipleCollections(IConfiguration cfg, ITestOutputHelper output)
        : base(cfg, output, true)
    {
    }
}


public abstract class DefaultTests : BaseFunctionalTestCase
{
    private readonly MemoryServerless _memory;


    protected DefaultTests(IConfiguration cfg, ITestOutputHelper output, bool multiCollection) : base(cfg, output)
    {
        Assert.False(string.IsNullOrEmpty(OpenAiConfig.APIKey), "OpenAI API Key is empty");

        if (multiCollection)
        {
            // this._config = this.MongoDbAtlasConfig;
            MongoDbAtlasConfig
                .WithSingleCollectionForVectorSearch(false)
                // Need to wait for atlas to grab the data from the collection and index.
                .WithAfterIndexCallback(async () => await Task.Delay(2000));

            MongoDbAtlasConfig.DatabaseName += "multicoll";
        }
        else
        {
            MongoDbAtlasConfig
                .WithSingleCollectionForVectorSearch(true)
                //Need to wait for atlas to grab the data from the collection and index.
                .WithAfterIndexCallback(async () => await Task.Delay(2000));
        }

        // Clear all content in any collection before running the test.
        var ash = new MongoDbAtlasSearchHelper(MongoDbAtlasConfig.ConnectionString, MongoDbAtlasConfig.DatabaseName);

        if (MongoDbAtlasConfig.UseSingleCollectionForVectorSearch)
        {
            //delete everything for every collection
            ash.DropAllDocumentsFromCollectionsAsync().Wait();
        }
        else
        {
            //drop the entire db to be sure we can start with a clean test.
            ash.DropDatabaseAsync().Wait();
        }

        _memory = new KernelMemoryBuilder()
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            // .WithAzureOpenAITextGeneration(this.AzureOpenAITextConfiguration)
            // .WithAzureOpenAITextEmbeddingGeneration(this.AzureOpenAIEmbeddingConfiguration)
            .WithMongoDbAtlasMemoryDb(MongoDbAtlasConfig)
            .Build<MemoryServerless>();
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItSupportsASingleFilter()
    {
        await FilteringTest.ItSupportsASingleFilter(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItSupportsMultipleFilters()
    {
        await FilteringTest.ItSupportsMultipleFilters(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItIgnoresEmptyFilters()
    {
        await FilteringTest.ItIgnoresEmptyFilters(_memory, Log, true);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItListsIndexes()
    {
        await IndexListTest.ItListsIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItNormalizesIndexNames()
    {
        await IndexListTest.ItNormalizesIndexNames(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItUsesDefaultIndexName()
    {
        await IndexListTest.ItUsesDefaultIndexName(_memory, Log, "default4tests");
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItDeletesIndexes()
    {
        await IndexDeletionTest.ItDeletesIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItHandlesMissingIndexesConsistently()
    {
        await MissingIndexTest.ItHandlesMissingIndexesConsistently(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItUploadsPDFDocsAndDeletes()
    {
        await DocumentUploadTest.ItUploadsPDFDocsAndDeletes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItSupportsTags()
    {
        await DocumentUploadTest.ItSupportsTags(_memory, Log);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task ItDownloadsPDFDocs()
    {
        await DocumentUploadTest.ItDownloadsPDFDocs(_memory, Log);
    }
}
