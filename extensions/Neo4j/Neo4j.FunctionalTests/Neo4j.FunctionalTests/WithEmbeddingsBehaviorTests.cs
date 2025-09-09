// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;
using Microsoft.Neo4j.FunctionalTests.TestHelpers;
using Neo4j.Driver;

namespace Microsoft.Neo4j.FunctionalTests;

public sealed class WithEmbeddingsBehaviorTests : BaseUnitTestCase, IAsyncDisposable
{
    private readonly Neo4jConfig _config;
    private readonly FakeEmbeddingGenerator _embeddingGenerator;
    private readonly IDriver _driver;
    private readonly Neo4jMemory _memory;


    public WithEmbeddingsBehaviorTests(ITestOutputHelper output) : base(output)
    {
        _config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "test_password",
            DatabaseName = "neo4j"
        };

        _embeddingGenerator = new FakeEmbeddingGenerator();
        _embeddingGenerator.Mock("test query", [0.1f, 0.2f, 0.3f]);
        _embeddingGenerator.Mock("sample text", [0.4f, 0.5f, 0.6f]);

        _driver = Neo4jTestHelper.CreateTestDriver(_config);
        _memory = new Neo4jMemory(_config, _embeddingGenerator);
    }


    [Fact]
    [Trait("Category", "FunctionalTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItDefaultsToWithEmbeddingsFalse()
    {
        // Arrange
        const string indexName = "test_embeddings_default";
        const string recordId = "test_record_1";
        float[] testVector = [0.1f, 0.2f, 0.3f];
        MemoryRecord record = new()
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "test content" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        try
        {
            // Act _ Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act _ Get similar records without specifying withEmbeddings (should default to false)
            List<(MemoryRecord, double)> results = await _memory.GetSimilarListAsync(indexName, "test query").ToListAsync();

            // Assert
            Assert.Single(results);
            (MemoryRecord memoryRecord, double score) = results[0];
            Assert.Equal(recordId, memoryRecord.Id);
            Assert.True(memoryRecord.Vector.Data.IsEmpty); // Vector should be empty when withEmbeddings=false (default)
            Assert.NotEmpty(memoryRecord.Payload);
            Assert.NotEmpty(memoryRecord.Tags);
        }
        finally
        {
            // Note: Comprehensive cleanup handled by DisposeAsync using Neo4jTestHelper
        }
    }


    [Fact]
    [Trait("Category", "FunctionalTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItReturnsVectorWhenWithEmbeddingsTrue()
    {
        // Arrange
        const string indexName = "test_embeddings_true";
        const string recordId = "test_record_2";
        float[] testVector = [0.4f, 0.5f, 0.6f];
        MemoryRecord record = new()
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "sample content" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        try
        {
            // Act _ Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act _ Get similar records with withEmbeddings=true
            List<(MemoryRecord, double)> results = await _memory.GetSimilarListAsync(indexName, "sample text", withEmbeddings: true).ToListAsync();

            // Assert
            Assert.Single(results);
            (MemoryRecord memoryRecord, double score) = results[0];
            Assert.Equal(recordId, memoryRecord.Id);
            Assert.False(memoryRecord.Vector.Data.IsEmpty); // Vector should be present when withEmbeddings=true
            Assert.Equal(testVector.Length, memoryRecord.Vector.Data.Length);
            Assert.NotEmpty(memoryRecord.Payload);
            Assert.NotEmpty(memoryRecord.Tags);
        }
        finally
        {
            // Note: Comprehensive cleanup handled by DisposeAsync using Neo4jTestHelper
        }
    }


    [Fact]
    [Trait("Category", "FunctionalTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItOmitsVectorWhenWithEmbeddingsFalseExplicit()
    {
        // Arrange
        const string indexName = "test_embeddings_false";
        const string recordId = "test_record_3";
        float[] testVector = [0.7f, 0.8f, 0.9f];
        MemoryRecord record = new()
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "explicit false test" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        try
        {
            // Act _ Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act _ Get similar records with withEmbeddings=false explicitly
            List<(MemoryRecord, double)> results = [];

            await foreach ((MemoryRecord, double) result in _memory.GetSimilarListAsync(indexName, "test query", withEmbeddings: false))
            {
                results.Add(result);
            }

            // Assert
            Assert.Single(results);
            (MemoryRecord memoryRecord, double score) = results[0];
            Assert.Equal(recordId, memoryRecord.Id);
            Assert.True(memoryRecord.Vector.Data.IsEmpty); // Vector should be empty when withEmbeddings=false
            Assert.NotEmpty(memoryRecord.Payload);
            Assert.NotEmpty(memoryRecord.Tags);
        }
        finally
        {
            // Note: Comprehensive cleanup handled by DisposeAsync using Neo4jTestHelper
        }
    }


    [Fact]
    [Trait("Category", "FunctionalTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesWithEmbeddingsInGetListAsync()
    {
        // Arrange
        const string indexName = "test_getlist_embeddings";
        const string recordId = "test_record_4";
        float[] testVector = [0.1f, 0.3f, 0.5f];
        MemoryRecord record = new()
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "getlist test" } },
            Tags = new TagCollection { { "category", "getlist" } }
        };

        try
        {
            // Act _ Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act _ Get list without embeddings (default)
            List<MemoryRecord> resultsWithoutEmbeddings = [];

            await foreach (MemoryRecord result in _memory.GetListAsync(indexName))
            {
                resultsWithoutEmbeddings.Add(result);
            }

            // Act _ Get list with embeddings
            List<MemoryRecord> resultsWithEmbeddings = [];

            await foreach (MemoryRecord result in _memory.GetListAsync(indexName, withEmbeddings: true))
            {
                resultsWithEmbeddings.Add(result);
            }

            // Assert _ Without embeddings
            Assert.Single(resultsWithoutEmbeddings);
            MemoryRecord recordWithoutEmbeddings = resultsWithoutEmbeddings[0];
            Assert.Equal(recordId, recordWithoutEmbeddings.Id);
            Assert.True(recordWithoutEmbeddings.Vector.Data.IsEmpty); // Vector should be empty

            // Assert _ With embeddings
            Assert.Single(resultsWithEmbeddings);
            MemoryRecord recordWithEmbeddings = resultsWithEmbeddings[0];
            Assert.Equal(recordId, recordWithEmbeddings.Id);
            Assert.False(recordWithEmbeddings.Vector.Data.IsEmpty); // Vector should be present
            Assert.Equal(testVector.Length, recordWithEmbeddings.Vector.Data.Length);
        }
        finally
        {
            // Note: Comprehensive cleanup handled by DisposeAsync using Neo4jTestHelper
        }
    }


    [Fact]
    [Trait("Category", "FunctionalTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItPreservesPayloadAndTagsRegardlessOfWithEmbeddings()
    {
        // Arrange
        const string indexName = "test_preserve_data";
        const string recordId = "test_record_5";
        float[] testVector = [0.2f, 0.4f, 0.6f];
        Dictionary<string, object> payload = new()
        {
            { "title", "Test Document" },
            { "content", "This is test content" },
            { "number", 42 }
        };
        TagCollection tags = new()
        {
            { "category", "test" },
            { "type", "document" },
            { "priority", "high" }
        };
        MemoryRecord record = new()
        {
            Id = recordId,
            Vector = testVector,
            Payload = payload,
            Tags = tags
        };

        try
        {
            // Act _ Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act _ Get records with and without embeddings
            List<(MemoryRecord, double)> resultsWithoutEmbeddings = await _memory.GetSimilarListAsync(indexName, "test query", withEmbeddings: false).ToListAsync();

            List<(MemoryRecord, double)> resultsWithEmbeddings = await _memory.GetSimilarListAsync(indexName, "test query", withEmbeddings: true).ToListAsync();

            // Assert _ Both should have same payload and tags
            Assert.Single(resultsWithoutEmbeddings);
            Assert.Single(resultsWithEmbeddings);

            MemoryRecord recordWithoutEmbeddings = resultsWithoutEmbeddings[0].Item1;
            MemoryRecord recordWithEmbeddings = resultsWithEmbeddings[0].Item1;

            // Verify payload preservation
            Assert.Equal(payload.Count, recordWithoutEmbeddings.Payload.Count);
            Assert.Equal(payload.Count, recordWithEmbeddings.Payload.Count);
            Assert.Equal("Test Document", recordWithoutEmbeddings.Payload["title"].ToString());
            Assert.Equal("Test Document", recordWithEmbeddings.Payload["title"].ToString());

            if (int.TryParse(recordWithoutEmbeddings.Payload["number"].ToString(), out int numberWithoutEmbeddings))
            {
                Assert.Equal(42, numberWithoutEmbeddings);
            }

            if (int.TryParse(recordWithEmbeddings.Payload["number"].ToString(), out int numberWithEmbeddings))
            {
                Assert.Equal(42, numberWithEmbeddings);
            }

            // Verify tags preservation
            Assert.Equal(tags.Count, recordWithoutEmbeddings.Tags.Count);
            Assert.Equal(tags.Count, recordWithEmbeddings.Tags.Count);
            Assert.Contains("test", recordWithoutEmbeddings.Tags["category"]);
            Assert.Contains("test", recordWithEmbeddings.Tags["category"]);
            Assert.Contains("high", recordWithoutEmbeddings.Tags["priority"]);
            Assert.Contains("high", recordWithEmbeddings.Tags["priority"]);
        }
        finally
        {
            // Note: Comprehensive cleanup handled by DisposeAsync using Neo4jTestHelper
        }
    }


    public async ValueTask DisposeAsync()
    {
        try
        {
            // Use Neo4jTestHelper for comprehensive cleanup
            await _driver.CleanupTestDataAsync(typeof(WithEmbeddingsBehaviorTests), _config);
        }
        catch (Exception ex)
        {
            // Log but don't fail disposal
            Console.WriteLine($"Warning: Failed to cleanup test data: {ex.Message}");
        }
        finally
        {
            _driver.Dispose();
            await _memory.DisposeAsync();
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _driver.Dispose();
            _memory.Dispose();
        }
        base.Dispose(disposing);
    }
}
