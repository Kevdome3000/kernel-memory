// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.FunctionalTests;

public class AdditionalFilteringTests : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;


    public AdditionalFilteringTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        var neo4jConfig = cfg.GetSection("KernelMemory:Services:Neo4j").Get<Neo4jConfig>() ?? new Neo4jConfig();

        _memory = new KernelMemoryBuilder()
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            //.WithOpenAI(this.OpenAiConfig)
            .WithAzureOpenAITextGeneration(AzureOpenAITextConfiguration)
            .WithAzureOpenAITextEmbeddingGeneration(AzureOpenAIEmbeddingConfiguration)
            .WithNeo4jMemoryDb(neo4jConfig)
            .Build<MemoryServerless>();
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
        var answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByDocument("1"));
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


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesComplexMultiValueTagScenarios()
    {
        // Arrange
        const string Q = "what programming languages are mentioned?";
        await _memory.ImportTextAsync("Python is great for data science",
            "doc1",
            new TagCollection { { "language", "python" }, { "category", "data" }, { "category", "science" } });
        await _memory.ImportTextAsync("Java is excellent for enterprise",
            "doc2",
            new TagCollection { { "language", "java" }, { "category", "enterprise" }, { "category", "backend" } });
        await _memory.ImportTextAsync("JavaScript runs everywhere",
            "doc3",
            new TagCollection { { "language", "javascript" }, { "category", "web" }, { "category", "frontend" } });

        // Act + Assert - Filter by single category value (should match multiple docs)
        var answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag("category", "data"));
        Assert.Contains("Python", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Java", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JavaScript", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - Filter by multiple category values using OR logic
        answer = await _memory.AskAsync(Q,
            filters:
            [
                MemoryFilters.ByTag("category", "web"),
                MemoryFilters.ByTag("category", "enterprise")
            ]);
        Assert.DoesNotContain("Python", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Java", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JavaScript", answer.Result, StringComparison.OrdinalIgnoreCase);

        // Act + Assert - Complex AND + OR logic
        answer = await _memory.AskAsync(Q,
            filters:
            [
                MemoryFilters.ByTag("language", "python").ByTag("category", "science"),
                MemoryFilters.ByTag("language", "java").ByTag("category", "backend")
            ]);
        Assert.Contains("Python", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Java", answer.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JavaScript", answer.Result, StringComparison.OrdinalIgnoreCase);
    }


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
        var answer = await _memory.AskAsync(Q, filter: MemoryFilters.ByTag(Constants.ReservedDocumentIdTag, "neo4j-doc"));
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
}
