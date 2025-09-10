// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;
using Microsoft.Neo4j.FunctionalTests.TestHelpers;
using Neo4j.Driver;

namespace Microsoft.Neo4j.FunctionalTests;

public class Neo4jSpecificTests : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;
    private readonly Neo4jConfig _neo4jConfig;
    private readonly IDriver _driver;


    public Neo4jSpecificTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
        _neo4jConfig = cfg.GetSection("KernelMemory:Services:Neo4j").Get<Neo4jConfig>() ?? new Neo4jConfig();

        _driver = Neo4jTestHelper.CreateTestDriver(_neo4jConfig);

        _memory = new KernelMemoryBuilder()
            .WithSearchClientConfig(new SearchClientConfig { EmptyAnswer = NotFound })
            .WithOpenAI(OpenAiConfig)
            .WithNeo4jMemoryDb(_neo4jConfig)
            .Build<MemoryServerless>(KernelMemoryBuilderBuildOptions.WithVolatileAndPersistentData);
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


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesDocumentImportAndSearch()
    {
        // Arrange
        const string indexName = "test_neo4j_import";
        const string documentId = "test_doc_1";
        const string testContent = "This is test content for Neo4j connector validation.";

        // Act _ Import document
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

        // Act _ Search for content
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
        const string indexName = "test_neo4j_filtering";
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

        // Act _ Search with category filter
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
    public async Task ItHandlesMultipleFiltersWithOrLogic()
    {
        // Arrange
        const string indexName = "test_neo4j_or_filters";
        const string doc1Id = "science_doc";
        const string doc2Id = "tech_doc";
        const string doc3Id = "art_doc";

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

        // Act _ Search with OR filters (science OR technology)
        MemoryAnswer scienceOrTechResult = await _memory.AskAsync(
            "What topics are covered?",
            filters:
            [
                MemoryFilters.ByTag("category", "science"),
                MemoryFilters.ByTag("category", "technology")
            ],
            index: indexName);

        // Act _ Search with single filter that should exclude art
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
        const string indexName1 = "test_index_mgmt_1";
        const string indexName2 = "test_index_mgmt_2";
        const string documentId = "index_mgmt_test";

        // Act _ Create indexes by importing documents
        await _memory.ImportTextAsync(
            "Content for first index.",
            documentId + "_1",
            new TagCollection { { "index", "first" } },
            indexName1);

        await _memory.ImportTextAsync(
            "Content for second index.",
            documentId + "_2",
            new TagCollection { { "index", "second" } },
            indexName2);

        // Act _ Verify indexes exist by searching
        MemoryAnswer result1 = await _memory.AskAsync("What content is here?", indexName1);
        MemoryAnswer result2 = await _memory.AskAsync("What content is here?", indexName2);

        // Assert _ Both indexes should work
        Assert.NotEqual(NotFound, result1.Result);
        Assert.NotEqual(NotFound, result2.Result);
        Assert.Contains("first", result1.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("second", result2.Result, StringComparison.OrdinalIgnoreCase);

        // Act _ Delete one index
        await _memory.DeleteIndexAsync(indexName1);

        // Act _ Try to search deleted index (should return not found)
        MemoryAnswer deletedIndexResult = await _memory.AskAsync("What content is here?", indexName1);

        // Assert _ Deleted index should return not found
        Assert.Equal(NotFound, deletedIndexResult.Result);

        // Act _ Verify other index still works
        MemoryAnswer stillWorkingResult = await _memory.AskAsync("What content is here?", indexName2);
        Assert.NotEqual(NotFound, stillWorkingResult.Result);

        // Cleanup
        await _memory.DeleteDocumentAsync(documentId + "_2", indexName2);
        await _memory.DeleteIndexAsync(indexName2);
    }


    [Fact]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesLargeDocuments()
    {
        // Arrange
        const string indexName = "test_neo4j_large_doc";
        const string documentId = "large_doc_test";

        // Create a large document with repeated content
        string largeContent = string.Join(" ",
            Enumerable.Repeat(
                "This is a large document with lots of content to test Neo4j handling of substantial text. " + "It contains information about various topics including science, technology, and research. " + "The document is designed to test the chunking and storage capabilities of the Neo4j connector.",
                50)); // Repeat 50 times to create substantial content

        // Act _ Import large document
        await _memory.ImportTextAsync(
            largeContent,
            documentId,
            new TagCollection { { "size", "large" }, { "test", "chunking" } },
            indexName);

        // Act _ Search for content
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
