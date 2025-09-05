// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;
using Neo4j.Driver;

namespace Microsoft.Neo4j.UnitTests;

public class WithEmbeddingsBehaviorTests : BaseUnitTestCase
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
            Password = "password"
        };

        _embeddingGenerator = new FakeEmbeddingGenerator();
        _embeddingGenerator.Mock("test query", new[] { 0.1f, 0.2f, 0.3f });
        _embeddingGenerator.Mock("sample text", new[] { 0.4f, 0.5f, 0.6f });

        _driver = GraphDatabase.Driver(_config.Uri, AuthTokens.Basic(_config.Username, _config.Password));
        _memory = new Neo4jMemory(_config, _embeddingGenerator);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItDefaultsToWithEmbeddingsFalse()
    {
        // Arrange
        const string indexName = "test-embeddings-default";
        const string recordId = "test-record-1";
        var testVector = new[] { 0.1f, 0.2f, 0.3f };
        var record = new MemoryRecord
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "test content" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        try
        {
            // Act - Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act - Get similar records without specifying withEmbeddings (should default to false)
            var results = new List<(MemoryRecord, double)>();

            await foreach (var result in _memory.GetSimilarListAsync(indexName, "test query"))
            {
                results.Add(result);
            }

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
            // Cleanup
            await CleanupIndexAsync(indexName);
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItReturnsVectorWhenWithEmbeddingsTrue()
    {
        // Arrange
        const string indexName = "test-embeddings-true";
        const string recordId = "test-record-2";
        var testVector = new[] { 0.4f, 0.5f, 0.6f };
        var record = new MemoryRecord
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "sample content" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        try
        {
            // Act - Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act - Get similar records with withEmbeddings=true
            var results = new List<(MemoryRecord, double)>();

            await foreach (var result in _memory.GetSimilarListAsync(indexName, "sample text", withEmbeddings: true))
            {
                results.Add(result);
            }

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
            // Cleanup
            await CleanupIndexAsync(indexName);
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItOmitsVectorWhenWithEmbeddingsFalseExplicit()
    {
        // Arrange
        const string indexName = "test-embeddings-false";
        const string recordId = "test-record-3";
        var testVector = new[] { 0.7f, 0.8f, 0.9f };
        var record = new MemoryRecord
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "explicit false test" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        try
        {
            // Act - Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act - Get similar records with withEmbeddings=false explicitly
            var results = new List<(MemoryRecord, double)>();

            await foreach (var result in _memory.GetSimilarListAsync(indexName, "test query", withEmbeddings: false))
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
            // Cleanup
            await CleanupIndexAsync(indexName);
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItHandlesWithEmbeddingsInGetListAsync()
    {
        // Arrange
        const string indexName = "test-getlist-embeddings";
        const string recordId = "test-record-4";
        var testVector = new[] { 0.1f, 0.3f, 0.5f };
        var record = new MemoryRecord
        {
            Id = recordId,
            Vector = testVector,
            Payload = new Dictionary<string, object> { { "content", "getlist test" } },
            Tags = new TagCollection { { "category", "getlist" } }
        };

        try
        {
            // Act - Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act - Get list without embeddings (default)
            var resultsWithoutEmbeddings = new List<MemoryRecord>();

            await foreach (var result in _memory.GetListAsync(indexName))
            {
                resultsWithoutEmbeddings.Add(result);
            }

            // Act - Get list with embeddings
            var resultsWithEmbeddings = new List<MemoryRecord>();

            await foreach (var result in _memory.GetListAsync(indexName, withEmbeddings: true))
            {
                resultsWithEmbeddings.Add(result);
            }

            // Assert - Without embeddings
            Assert.Single(resultsWithoutEmbeddings);
            var recordWithoutEmbeddings = resultsWithoutEmbeddings[0];
            Assert.Equal(recordId, recordWithoutEmbeddings.Id);
            Assert.True(recordWithoutEmbeddings.Vector.Data.IsEmpty); // Vector should be empty

            // Assert - With embeddings
            Assert.Single(resultsWithEmbeddings);
            var recordWithEmbeddings = resultsWithEmbeddings[0];
            Assert.Equal(recordId, recordWithEmbeddings.Id);
            Assert.False(recordWithEmbeddings.Vector.Data.IsEmpty); // Vector should be present
            Assert.Equal(testVector.Length, recordWithEmbeddings.Vector.Data.Length);
        }
        finally
        {
            // Cleanup
            await CleanupIndexAsync(indexName);
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public async Task ItPreservesPayloadAndTagsRegardlessOfWithEmbeddings()
    {
        // Arrange
        const string indexName = "test-preserve-data";
        const string recordId = "test-record-5";
        var testVector = new[] { 0.2f, 0.4f, 0.6f };
        var payload = new Dictionary<string, object>
        {
            { "title", "Test Document" },
            { "content", "This is test content" },
            { "number", 42 }
        };
        var tags = new TagCollection
        {
            { "category", "test" },
            { "type", "document" },
            { "priority", "high" }
        };
        var record = new MemoryRecord
        {
            Id = recordId,
            Vector = testVector,
            Payload = payload,
            Tags = tags
        };

        try
        {
            // Act - Create index and upsert record
            await _memory.CreateIndexAsync(indexName, testVector.Length);
            await _memory.UpsertAsync(indexName, record);

            // Act - Get records with and without embeddings
            var resultsWithoutEmbeddings = new List<(MemoryRecord, double)>();

            await foreach (var result in _memory.GetSimilarListAsync(indexName, "test query", withEmbeddings: false))
            {
                resultsWithoutEmbeddings.Add(result);
            }

            var resultsWithEmbeddings = new List<(MemoryRecord, double)>();

            await foreach (var result in _memory.GetSimilarListAsync(indexName, "test query", withEmbeddings: true))
            {
                resultsWithEmbeddings.Add(result);
            }

            // Assert - Both should have same payload and tags
            Assert.Single(resultsWithoutEmbeddings);
            Assert.Single(resultsWithEmbeddings);

            var recordWithoutEmbeddings = resultsWithoutEmbeddings[0].Item1;
            var recordWithEmbeddings = resultsWithEmbeddings[0].Item1;

            // Verify payload preservation
            Assert.Equal(payload.Count, recordWithoutEmbeddings.Payload.Count);
            Assert.Equal(payload.Count, recordWithEmbeddings.Payload.Count);
            Assert.Equal("Test Document", recordWithoutEmbeddings.Payload["title"]);
            Assert.Equal("Test Document", recordWithEmbeddings.Payload["title"]);
            Assert.Equal(42, recordWithoutEmbeddings.Payload["number"]);
            Assert.Equal(42, recordWithEmbeddings.Payload["number"]);

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
            // Cleanup
            await CleanupIndexAsync(indexName);
        }
    }


    private async Task CleanupIndexAsync(string indexName)
    {
        try
        {
            await _memory.DeleteIndexAsync(indexName);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _driver?.Dispose();
            _memory?.Dispose();
        }
        base.Dispose(disposing);
    }
}
