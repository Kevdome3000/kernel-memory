// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KM.Core.FunctionalTests.DefaultTestCases;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.FunctionalTests;

public class DefaultTests : BaseFunctionalTestCase
{
    private readonly MemoryServerless _memory;


    public DefaultTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        Assert.False(string.IsNullOrEmpty(OpenAiConfig.APIKey));

        var neo4jConfig = cfg.GetSection("KernelMemory:Services:Neo4j").Get<Neo4jConfig>() ?? new Neo4jConfig();

        _memory = new KernelMemoryBuilder()
            .With(new KernelMemoryConfig { DefaultIndexName = "default4tests" })
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            // .WithAzureOpenAITextGeneration(this.AzureOpenAITextConfiguration)
            // .WithAzureOpenAITextEmbeddingGeneration(this.AzureOpenAIEmbeddingConfiguration)
            .WithNeo4jMemoryDb(neo4jConfig)
            .Build<MemoryServerless>();
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItSupportsASingleFilter()
    {
        await FilteringTest.ItSupportsASingleFilter(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItSupportsMultipleFilters()
    {
        await FilteringTest.ItSupportsMultipleFilters(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItIgnoresEmptyFilters()
    {
        await FilteringTest.ItIgnoresEmptyFilters(_memory, Log, true);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItDoesntFailIfTheIndexExistsAlready()
    {
        await IndexCreationTest.ItDoesntFailIfTheIndexExistsAlready(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItListsIndexes()
    {
        await IndexListTest.ItListsIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItNormalizesIndexNames()
    {
        await IndexListTest.ItNormalizesIndexNames(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItUsesDefaultIndexName()
    {
        await IndexListTest.ItUsesDefaultIndexName(_memory, Log, "default4tests");
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItDeletesIndexes()
    {
        await IndexDeletionTest.ItDeletesIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesMissingIndexesConsistently()
    {
        await MissingIndexTest.ItHandlesMissingIndexesConsistently(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItUploadsPDFDocsAndDeletes()
    {
        await DocumentUploadTest.ItUploadsPDFDocsAndDeletes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItSupportsTags()
    {
        await DocumentUploadTest.ItSupportsTags(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItDownloadsPDFDocs()
    {
        await DocumentUploadTest.ItDownloadsPDFDocs(_memory, Log);
    }
}
