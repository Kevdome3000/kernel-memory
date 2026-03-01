// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryDb.SQLServer;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.Core.FunctionalTests.DefaultTestCases;
using Microsoft.KM.TestHelpers;

namespace Microsoft.SQLServer.FunctionalTests;

public class DefaultTests : BaseFunctionalTestCase
{
    private readonly MemoryServerless _memory;
    private readonly IMemoryDb _memoryDb;


    public DefaultTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        Assert.False(string.IsNullOrEmpty(OpenAiConfig.APIKey));

        SqlServerConfig sqlServerConfig = cfg.GetSection("KernelMemory:Services:SqlServer").Get<SqlServerConfig>()!;

        var builder = new KernelMemoryBuilder();

        _memory = builder
            .With(new KernelMemoryConfig { DefaultIndexName = "default4tests" })
            .Configure(kmb => kmb.Services.AddLogging(b => { b.AddConsole().SetMinimumLevel(LogLevel.Trace); }))
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            // .WithAzureOpenAITextGeneration(this.AzureOpenAITextConfiguration)
            // .WithAzureOpenAITextEmbeddingGeneration(this.AzureOpenAIEmbeddingConfiguration)
            .WithSqlServerMemoryDb(sqlServerConfig)
            .Build<MemoryServerless>();

        var serviceProvider = builder.Services.BuildServiceProvider();
        _memoryDb = serviceProvider.GetService<IMemoryDb>()!;
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItSupportsASingleFilter()
    {
        await FilteringTest.ItSupportsASingleFilter(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItSupportsMultipleFilters()
    {
        await FilteringTest.ItSupportsMultipleFilters(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItIgnoresEmptyFilters()
    {
        await FilteringTest.ItIgnoresEmptyFilters(_memory, Log, true);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItDoesntFailIfTheIndexExistsAlready()
    {
        await IndexCreationTest.ItDoesntFailIfTheIndexExistsAlready(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItListsIndexes()
    {
        await IndexListTest.ItListsIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItNormalizesIndexNames()
    {
        await IndexListTest.ItNormalizesIndexNames(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItUsesDefaultIndexName()
    {
        await IndexListTest.ItUsesDefaultIndexName(_memory, Log, "default4tests");
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItDeletesIndexes()
    {
        await IndexDeletionTest.ItDeletesIndexes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItHandlesMissingIndexesConsistently()
    {
        await MissingIndexTest.ItHandlesMissingIndexesConsistently(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItUploadsPDFDocsAndDeletes()
    {
        await DocumentUploadTest.ItUploadsPDFDocsAndDeletes(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItSupportsTags()
    {
        await DocumentUploadTest.ItSupportsTags(_memory, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItDeletesRecords()
    {
        await RecordDeletionTest.ItDeletesRecords(_memory, _memoryDb, Log);
    }


    [Fact]
    [Trait("Category", "SQLServer")]
    public async Task ItCanImportDocumentWithManyTagsAtATime()
    {
        const string Id = "ItCanImportDocumentWithManyTagsAtATime-file1-NASA-news.pdf";

        var tags = new TagCollection
        {
            { "type", "news" },
            { "type", "test" },
            { "ext", "pdf" }
        };

        for (int i = 0; i < 100; i++)
        {
            tags.AddSyntheticTag($"tagTest{i}");
        }

        await _memory.ImportDocumentAsync(
            "file1-NASA-news.pdf",
            Id,
            tags,
            steps: Constants.PipelineWithoutSummary);

        while (!await _memory.IsDocumentReadyAsync(Id))
        {
            Log("Waiting for memory ingestion to complete...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
