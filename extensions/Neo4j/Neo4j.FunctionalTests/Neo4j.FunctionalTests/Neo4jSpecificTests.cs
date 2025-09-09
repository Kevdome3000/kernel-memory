// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.FunctionalTests;

public class Neo4jSpecificTests : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;


    public Neo4jSpecificTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        Neo4jConfig neo4jConfig = cfg.GetSection("KernelMemory:Services:Neo4j").Get<Neo4jConfig>() ?? new Neo4jConfig();

        _memory = new KernelMemoryBuilder()
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            .WithNeo4jMemoryDb(neo4jConfig)
            .Build<MemoryServerless>();
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesDocumentImportAndSearch()
    {
        // Arrange
        const string indexName = "test-neo4j-import";
        const string documentId = "test-doc-1";
        const string testContent = "This is test content for Neo4j connector validation.";

        // Act - Import document
        await _memory.ImportTextAsync(
            testContent,
            documentId,
            new TagCollection { { "type", "test" }, { "category", "neo4j" } },
            indexName);

        // Wait for processing
        while (!await _memory.IsDocumentReadyAsync(documentId, indexName))
        {
            Log("Waiting for document processing...");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Act - Search for content
        MemoryAnswer searchResult = await _memory.AskAsync("What is the test content?", indexName);

        // Assert
        Assert.NotEqual(NotFound, searchResult.Result);
        Assert.Contains("test content", searchResult.Result, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        await _memory.DeleteDocumentAsync(documentId, indexName);
        await _memory.DeleteIndexAsync(indexName);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesTagBasedFiltering()
    {
        // Arrange
        const string indexName = "test-neo4j-filtering";
        const string doc1Id = "doc1";
        const string doc2Id = "doc2";

        // Import documents with different tags
        await _memory.ImportTextAsync(
            "This document is about cats and animals.",
            doc1Id,
            new TagCollection { { "category", "animals" }, { "type", "mammals" } },
            indexName);

        await _memory.ImportTextAsync(
            "This document is about cars and vehicles.",
            doc2Id,
            new TagCollection { { "category", "vehicles" }, { "type", "transportation" } },
            indexName);

        // Wait for processing
        while (!await _memory.IsDocumentReadyAsync(doc1Id, indexName) || !await _memory.IsDocumentReadyAsync(doc2Id, indexName))
        {
            Log("Waiting for document processing...");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Act - Search with category filter
        MemoryAnswer animalResult = await _memory.AskAsync(
            "What is this about?",
            filter: MemoryFilters.ByTag("category", "animals"),
            index: indexName);

        MemoryAnswer vehicleResult = await _memory.AskAsync(
            "What is this about?",
            filter: MemoryFilters.ByTag("category", "vehicles"),
            index: indexName);

        // Assert
        Assert.Contains("cats", animalResult.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cars", animalResult.Result, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("cars", vehicleResult.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cats", vehicleResult.Result, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        await _memory.DeleteDocumentAsync(doc1Id, indexName);
        await _memory.DeleteDocumentAsync(doc2Id, indexName);
        await _memory.DeleteIndexAsync(indexName);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesComplexIndexNames()
    {
        // Arrange
        const string complexIndexName = "My Complex/Index Name With.Special_Characters:Test";
        const string documentId = "complex-index-test";
        const string testContent = "Testing complex index name handling in Neo4j.";

        // Act - Import document with complex index name
        await _memory.ImportTextAsync(
            testContent,
            documentId,
            new TagCollection { { "test", "complex-naming" } },
            complexIndexName);

        // Wait for processing
        while (!await _memory.IsDocumentReadyAsync(documentId, complexIndexName))
        {
            Log("Waiting for document processing...");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Act - Search using the same complex index name
        MemoryAnswer searchResult = await _memory.AskAsync("What is being tested?", complexIndexName);

        // Assert
        Assert.NotEqual(NotFound, searchResult.Result);
        Assert.Contains("complex", searchResult.Result, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        await _memory.DeleteDocumentAsync(documentId, complexIndexName);
        await _memory.DeleteIndexAsync(complexIndexName);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesMultipleFiltersWithOrLogic()
    {
        // Arrange
        const string indexName = "test-neo4j-or-filters";
        const string doc1Id = "science-doc";
        const string doc2Id = "tech-doc";
        const string doc3Id = "art-doc";

        // Import documents with different categories
        await _memory.ImportTextAsync(
            "This document discusses quantum physics and scientific research.",
            doc1Id,
            new TagCollection { { "category", "science" }, { "level", "advanced" } },
            indexName);

        await _memory.ImportTextAsync(
            "This document covers software development and programming.",
            doc2Id,
            new TagCollection { { "category", "technology" }, { "level", "intermediate" } },
            indexName);

        await _memory.ImportTextAsync(
            "This document explores modern art and creative expression.",
            doc3Id,
            new TagCollection { { "category", "art" }, { "level", "beginner" } },
            indexName);

        // Wait for processing
        while (!await _memory.IsDocumentReadyAsync(doc1Id, indexName) || !await _memory.IsDocumentReadyAsync(doc2Id, indexName) || !await _memory.IsDocumentReadyAsync(doc3Id, indexName))
        {
            Log("Waiting for document processing...");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Act - Search with OR filters (science OR technology)
        MemoryAnswer scienceOrTechResult = await _memory.AskAsync(
            "What topics are covered?",
            filters:
            [
                MemoryFilters.ByTag("category", "science"),
                MemoryFilters.ByTag("category", "technology")
            ],
            index: indexName);

        // Act - Search with single filter that should exclude art
        MemoryAnswer artOnlyResult = await _memory.AskAsync(
            "What topics are covered?",
            filter: MemoryFilters.ByTag("category", "art"),
            index: indexName);

        // Assert
        // Science OR Technology should include both but not art
        string scienceTechResponse = scienceOrTechResult.Result;
        Assert.True(
            scienceTechResponse.Contains("quantum", StringComparison.OrdinalIgnoreCase) || scienceTechResponse.Contains("software", StringComparison.OrdinalIgnoreCase),
            "Should contain science or technology content");

        // Art only should contain art content
        Assert.Contains("art", artOnlyResult.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quantum", artOnlyResult.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("software", artOnlyResult.Result, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        await _memory.DeleteDocumentAsync(doc1Id, indexName);
        await _memory.DeleteDocumentAsync(doc2Id, indexName);
        await _memory.DeleteDocumentAsync(doc3Id, indexName);
        await _memory.DeleteIndexAsync(indexName);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesIndexManagement()
    {
        // Arrange
        const string indexName1 = "test-index-mgmt-1";
        const string indexName2 = "test-index-mgmt-2";
        const string documentId = "index-mgmt-test";

        // Act - Create indexes by importing documents
        await _memory.ImportTextAsync(
            "Content for first index.",
            documentId + "-1",
            new TagCollection { { "index", "first" } },
            indexName1);

        await _memory.ImportTextAsync(
            "Content for second index.",
            documentId + "-2",
            new TagCollection { { "index", "second" } },
            indexName2);

        // Wait for processing
        while (!await _memory.IsDocumentReadyAsync(documentId + "-1", indexName1) || !await _memory.IsDocumentReadyAsync(documentId + "-2", indexName2))
        {
            Log("Waiting for document processing...");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Act - Verify indexes exist by searching
        MemoryAnswer result1 = await _memory.AskAsync("What content is here?", indexName1);
        MemoryAnswer result2 = await _memory.AskAsync("What content is here?", indexName2);

        // Assert - Both indexes should work
        Assert.NotEqual(NotFound, result1.Result);
        Assert.NotEqual(NotFound, result2.Result);
        Assert.Contains("first", result1.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("second", result2.Result, StringComparison.OrdinalIgnoreCase);

        // Act - Delete one index
        await _memory.DeleteIndexAsync(indexName1);

        // Act - Try to search deleted index (should return not found)
        MemoryAnswer deletedIndexResult = await _memory.AskAsync("What content is here?", indexName1);

        // Assert - Deleted index should return not found
        Assert.Equal(NotFound, deletedIndexResult.Result);

        // Act - Verify other index still works
        MemoryAnswer stillWorkingResult = await _memory.AskAsync("What content is here?", indexName2);
        Assert.NotEqual(NotFound, stillWorkingResult.Result);

        // Cleanup
        await _memory.DeleteDocumentAsync(documentId + "-2", indexName2);
        await _memory.DeleteIndexAsync(indexName2);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesLargeDocuments()
    {
        // Arrange
        const string indexName = "test-neo4j-large-doc";
        const string documentId = "large-doc-test";

        // Create a large document with repeated content
        string largeContent = string.Join(" ",
            Enumerable.Repeat(
                "This is a large document with lots of content to test Neo4j handling of substantial text. " + "It contains information about various topics including science, technology, and research. " + "The document is designed to test the chunking and storage capabilities of the Neo4j connector.",
                50)); // Repeat 50 times to create substantial content

        // Act - Import large document
        await _memory.ImportTextAsync(
            largeContent,
            documentId,
            new TagCollection { { "size", "large" }, { "test", "chunking" } },
            indexName);

        // Wait for processing
        while (!await _memory.IsDocumentReadyAsync(documentId, indexName))
        {
            Log("Waiting for large document processing...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // Act - Search for content
        MemoryAnswer searchResult = await _memory.AskAsync("What topics are covered in this large document?", indexName);

        // Assert
        Assert.NotEqual(NotFound, searchResult.Result);
        Assert.True(
            searchResult.Result.Contains("science", StringComparison.OrdinalIgnoreCase) || searchResult.Result.Contains("technology", StringComparison.OrdinalIgnoreCase) || searchResult.Result.Contains("research", StringComparison.OrdinalIgnoreCase),
            "Should contain content from the large document");

        // Cleanup
        await _memory.DeleteDocumentAsync(documentId, indexName);
        await _memory.DeleteIndexAsync(indexName);
    }
}
