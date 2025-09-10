using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KM.Core.FunctionalTests.DefaultTestCases;
using Microsoft.KM.TestHelpers;
using Microsoft.Neo4j.FunctionalTests.TestHelpers;
using Neo4j.Driver;

namespace Microsoft.Neo4j.FunctionalTests;

public class DefaultTests : BaseFunctionalTestCase
{
    private readonly MemoryServerless _memory;
    private readonly Neo4jConfig _neo4jConfig;
    private readonly IDriver _driver;


    public DefaultTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        Assert.False(string.IsNullOrEmpty(OpenAiConfig.APIKey));

        _neo4jConfig = cfg.GetSection("KernelMemory:Services:Neo4j").Get<Neo4jConfig>() ?? new Neo4jConfig();

        _driver = Neo4jTestHelper.CreateTestDriver(_neo4jConfig);

        _memory = new KernelMemoryBuilder()
            .With(new KernelMemoryConfig { DefaultIndexName = "default4tests" })
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            // .WithAzureOpenAITextGeneration(this.AzureOpenAITextConfiguration)
            // .WithAzureOpenAITextEmbeddingGeneration(this.AzureOpenAIEmbeddingConfiguration)
            .WithNeo4jMemoryDb(_neo4jConfig)
            .Build<MemoryServerless>(KernelMemoryBuilderBuildOptions.WithVolatileAndPersistentData);
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
        await IndexListTest.ItListsIndexes(_memory, Log, "_");
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItNormalizesIndexNames()
    {
        await IndexListTest.ItNormalizesIndexNames(_memory, Log, "_");
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
        await MissingIndexTest.ItHandlesMissingIndexesConsistently(_memory, Log, "_");
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


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _driver.PerformFullCleanupAsync(_neo4jConfig).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to cleanup test data: {ex.Message}");
            }
            finally
            {
                _driver.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
