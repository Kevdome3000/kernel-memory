using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;
using Microsoft.Neo4j.FunctionalTests.TestHelpers;
using Neo4j.Driver;

namespace Microsoft.Neo4j.FunctionalTests;

public class AdditionalFilteringTests : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;
    private readonly Neo4jConfig _neo4jConfig;
    private readonly IDriver _driver;


    public AdditionalFilteringTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        _neo4jConfig = cfg.GetSection("KernelMemory:Services:Neo4j").Get<Neo4jConfig>() ?? new Neo4jConfig();

        _driver = Neo4jTestHelper.CreateTestDriver(_neo4jConfig);

        _memory = new KernelMemoryBuilder()
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            //.WithAzureOpenAITextGeneration(AzureOpenAITextConfiguration)
            //.WithAzureOpenAITextEmbeddingGeneration(AzureOpenAIEmbeddingConfiguration)
            .WithNeo4jMemoryDb(_neo4jConfig)
            .Build<MemoryServerless>(KernelMemoryBuilderBuildOptions.WithVolatileAndPersistentData);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItFiltersSourcesCorrectly()
    {
        // Arrange
        const string Q = "in one or two words, what colors should I choose?";
        await _memory.ImportTextAsync("green is a great color", "1", new TagCollection { { "user", "hulk" } });
        await _memory.ImportTextAsync("red is a great color", "2", new TagCollection { { "user", "flash" } });

        // Act + Assert - See only memory about Green color
        MemoryAnswer answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("1"));
        Assert.Contains("green", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("1").ByTag("user", "hulk"));
        Assert.Contains("green", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag("user", "hulk"));
        Assert.Contains("green", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filters: [MemoryFilters.ByTag("user", "x"), MemoryFilters.ByTag("user", "hulk")]);
        Assert.Contains("green", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - See only memory about Red color
        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("2"));
        Assert.Contains("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("2").ByTag("user", "flash"));
        Assert.Contains("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag("user", "flash"));
        Assert.Contains("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filters: [MemoryFilters.ByTag("user", "x"), MemoryFilters.ByTag("user", "flash")]);
        Assert.Contains("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - See both memories
        answer = await _memory.AskAsync(Q, filters: [MemoryFilters.ByTag("user", "hulk"), MemoryFilters.ByTag("user", "flash")]);
        Assert.Contains("green", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("red", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - See no memories about colors
        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("1").ByTag("user", "flash"));
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("2").ByTag("user", "hulk"));
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);

        answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag("user", "x"));
        Assert.DoesNotContain("red", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green", answer.Result, StringComparison.OrdinalIgnoreCase);
    }


    // [Fact]
    // [Trait("Category", "Neo4j")]
    // public async Task ItHandlesComplexMultiValueTagScenarios()
    // {
    //     // Arrange
    //     const string Q = "what programming languages are mentioned?";
    //     const string indexName = "test_neo4j_additional_multi";
    //     var collection_1 = new TagCollection { { "language", "python" }, { "category", "data" } };
    //     collection_1.Add("category", "science");
    //
    //     var collection_2 = new TagCollection { { "language", "java" }, { "category", "enterprise" } };
    //     collection_2.Add("category", "backend");
    //
    //     var collection_3 = new TagCollection { { "language", "javascript" }, { "category", "web" } };
    //     collection_3.Add("category", "frontend");
    //
    //     await _memory.ImportTextAsync("Python is great for data science",
    //         "doc1",
    //         collection_1,
    //         indexName);
    //
    //     await _memory.ImportTextAsync("Java is excellent for enterprise",
    //         "doc2",
    //         collection_2,
    //         indexName);
    //     await _memory.ImportTextAsync("JavaScript runs everywhere",
    //         "doc3",
    //         collection_3,
    //         indexName);
    //
    //     // Wait for processing
    //     while (!await _memory.IsDocumentReadyAsync("doc1", indexName)
    //         || !await _memory.IsDocumentReadyAsync("doc2", indexName)
    //         || !await _memory.IsDocumentReadyAsync("doc3", indexName))
    //     {
    //         Log("Waiting for document processing...");
    //         await Task.Delay(TimeSpan.FromSeconds(1));
    //     }
    //
    //     // Act + Assert - Filter by single category value (should match multiple docs)
    //     MemoryAnswer answer = new();
    //
    //     for (int retry = 0; retry < 10; retry++)
    //     {
    //         answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag("category", "data"), index: indexName);
    //
    //         if (!string.Equals(answer.Result, NotFound, StringComparison.OrdinalIgnoreCase))
    //         {
    //             break;
    //         }
    //         Log("Waiting for vector index to return results...");
    //         await Task.Delay(TimeSpan.FromSeconds(1));
    //     }
    //
    //     Assert.Contains("Python", answer.Result, StringComparison.OrdinalIgnoreCase);
    //     Assert.DoesNotContain("Java", answer.Result, StringComparison.OrdinalIgnoreCase);
    //     Assert.DoesNotContain("JavaScript", answer.Result, StringComparison.OrdinalIgnoreCase);
    //
    //     // Act + Assert - Filter by multiple category values using OR logic
    //     answer = await _memory.AskAsync(Q,
    //         filters:
    //         [
    //             MemoryFilters.ByTag("category", "web"),
    //             MemoryFilters.ByTag("category", "enterprise")
    //         ],
    //         index: indexName);
    //     Assert.DoesNotContain("Python", answer.Result, StringComparison.OrdinalIgnoreCase);
    //     Assert.Contains("Java", answer.Result, StringComparison.OrdinalIgnoreCase);
    //     Assert.Contains("JavaScript", answer.Result, StringComparison.OrdinalIgnoreCase);
    //
    //     // Act + Assert - Complex AND + OR logic
    //     answer = await _memory.AskAsync(Q,
    //         filters:
    //         [
    //             MemoryFilters.ByTag("language", "python").ByTag("category", "science"),
    //             MemoryFilters.ByTag("language", "java").ByTag("category", "backend")
    //         ],
    //         index: indexName);
    //     Assert.Contains("Python", answer.Result, StringComparison.OrdinalIgnoreCase);
    //     Assert.Contains("Java", answer.Result, StringComparison.OrdinalIgnoreCase);
    //     Assert.DoesNotContain("JavaScript", answer.Result, StringComparison.OrdinalIgnoreCase);
    // }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesReservedTagsCorrectly()
    {
        // Arrange
        const string Q = "what is the content about?";
        await _memory.ImportTextAsync("Neo4j is a graph database",
            "neo4j-doc",
            new TagCollection
            {
                { "type", "database" },
                { Constants.ReservedDocumentIdTag, "neo4j-doc" },
                { "technology", "graph" }
            });
        await _memory.ImportTextAsync("PostgreSQL is a relational database",
            "postgres-doc",
            new TagCollection
            {
                { "type", "database" },
                { Constants.ReservedDocumentIdTag, "postgres-doc" },
                { "technology", "relational" }
            });

        // Act + Assert - Filter by reserved document ID tag
        MemoryAnswer answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag(Constants.ReservedDocumentIdTag, "neo4j-doc"));
        Assert.Contains("Neo4j", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PostgreSQL", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - Combine reserved tag with regular tags
        answer = await _memory.AskAsync(Q,
            filter: MemoryFilters
                .ByTag(Constants.ReservedDocumentIdTag, "postgres-doc")
                .ByTag("technology", "relational"));
        Assert.DoesNotContain("Neo4j", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PostgreSQL", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - OR logic with reserved tags
        answer = await _memory.AskAsync(Q,
            filters:
            [
                MemoryFilters.ByTag(Constants.ReservedDocumentIdTag, "neo4j-doc"),
                MemoryFilters.ByTag(Constants.ReservedDocumentIdTag, "postgres-doc")
            ]);
        Assert.Contains("Neo4j", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PostgreSQL", answer.Result, StringComparison.OrdinalIgnoreCase);
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
