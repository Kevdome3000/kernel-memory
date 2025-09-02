// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KM.Core.FunctionalTests.DefaultTestCases;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Redis.FunctionalTests;

public class DefaultTests : BaseFunctionalTestCase
{
    private readonly MemoryServerless _memory;


    public DefaultTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        Assert.False(string.IsNullOrEmpty(OpenAiConfig.APIKey));

        _memory = new KernelMemoryBuilder()
            .With(new KernelMemoryConfig { DefaultIndexName = "default4tests" })
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            .WithRedisMemoryDb(RedisConfig)
            .Build<MemoryServerless>();
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItSupportsASingleFilter()
    {
        await FilteringTest.ItSupportsASingleFilter(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItSupportsMultipleFilters()
    {
        await FilteringTest.ItSupportsMultipleFilters(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItIgnoresEmptyFilters()
    {
        await FilteringTest.ItIgnoresEmptyFilters(_memory, Log, true);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItDoesntFailIfTheIndexExistsAlready()
    {
        await IndexCreationTest.ItDoesntFailIfTheIndexExistsAlready(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItListsIndexes()
    {
        await IndexListTest.ItListsIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItNormalizesIndexNames()
    {
        await IndexListTest.ItNormalizesIndexNames(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItUsesDefaultIndexName()
    {
        await IndexListTest.ItUsesDefaultIndexName(_memory, Log, "default4tests");
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItDeletesIndexes()
    {
        await IndexDeletionTest.ItDeletesIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItHandlesMissingIndexesConsistently()
    {
        await MissingIndexTest.ItHandlesMissingIndexesConsistently(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItUploadsPDFDocsAndDeletes()
    {
        await DocumentUploadTest.ItUploadsPDFDocsAndDeletes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItSupportsTags()
    {
        await DocumentUploadTest.ItSupportsTags(_memory, Log);
    }


    [Fact]
    [Trait("Category", "Redis")]
    public async Task ItDownloadsPDFDocs()
    {
        await DocumentUploadTest.ItDownloadsPDFDocs(_memory, Log);
    }
}
