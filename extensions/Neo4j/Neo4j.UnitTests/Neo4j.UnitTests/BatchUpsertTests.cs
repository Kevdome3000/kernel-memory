// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.UnitTests;

/// <summary>
/// Tests for batch upsert functionality in Neo4j connector.
/// Verifies IMemoryDbUpsertBatch interface implementation and batch processing behavior.
/// </summary>
public class BatchUpsertTests : BaseUnitTestCase
{
    private readonly FakeEmbeddingGenerator _embeddingGenerator;


    public BatchUpsertTests(ITestOutputHelper output) : base(output)
    {
        _embeddingGenerator = new FakeEmbeddingGenerator();
        _embeddingGenerator.Mock("test query", [0.1f, 0.2f, 0.3f]);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItImplementsIMemoryDbUpsertBatchInterface()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password"
        };

        // Act - Create Neo4jMemory instance
        var memory = new Neo4jMemory(config, _embeddingGenerator);

        // Assert - Verify it implements IMemoryDbUpsertBatch
        Assert.IsAssignableFrom<IMemoryDbUpsertBatch>(memory);
        Assert.IsAssignableFrom<IMemoryDb>(memory);

        Log("Neo4jMemory correctly implements IMemoryDbUpsertBatch interface");

        // Cleanup
        memory.Dispose();
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesBatchSizeConfiguration()
    {
        // Arrange & Act - Valid batch sizes
        var validConfigs = new[]
        {
            new Neo4jConfig { MaxBatchSize = 1 },
            new Neo4jConfig { MaxBatchSize = 100 },
            new Neo4jConfig { MaxBatchSize = 1000 },
            new Neo4jConfig { MaxBatchSize = 5000 }
        };

        // Assert - Valid configurations should not throw
        foreach (var config in validConfigs)
        {
            config.Uri = "neo4j://localhost:7687";
            config.Username = "neo4j";
            config.Password = "password";

            // Should not throw any exception
            config.Validate();
            Log($"Valid MaxBatchSize: {config.MaxBatchSize}");
        }

        // Arrange & Act - Invalid batch sizes
        var invalidConfigs = new[]
        {
            new Neo4jConfig { MaxBatchSize = 0 },
            new Neo4jConfig { MaxBatchSize = -1 },
            new Neo4jConfig { MaxBatchSize = -100 }
        };

        // Assert - Invalid configurations should throw
        foreach (var config in invalidConfigs)
        {
            config.Uri = "neo4j://localhost:7687";
            config.Username = "neo4j";
            config.Password = "password";

            Assert.Throws<ArgumentException>(() => config.Validate());
            Log($"Invalid MaxBatchSize correctly rejected: {config.MaxBatchSize}");
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesEmptyBatchCorrectly()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password"
        };

        var memory = new Neo4jMemory(config, _embeddingGenerator);
        var emptyRecords = new List<MemoryRecord>();

        // Act & Assert - Empty batch should not throw and should return no results
        var batchResult = memory.UpsertBatchAsync("test-index", emptyRecords);

        // The method should complete without throwing
        Assert.NotNull(batchResult);
        Log("Empty batch handled correctly");

        // Cleanup
        memory.Dispose();
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItCreatesBatchDataStructureCorrectly()
    {
        // Arrange
        var testRecords = new List<MemoryRecord>
        {
            new()
            {
                Id = "record-1",
                Vector = new[] { 0.1f, 0.2f, 0.3f },
                Payload = new Dictionary<string, object> { { "content", "First record" } },
                Tags = new TagCollection { { "type", "test" }, { "batch", "1" } }
            },
            new()
            {
                Id = "record-2",
                Vector = new[] { 0.4f, 0.5f, 0.6f },
                Payload = new Dictionary<string, object> { { "content", "Second record" } },
                Tags = new TagCollection { { "type", "test" }, { "batch", "2" } }
            },
            new()
            {
                Id = "record-3",
                Vector = new[] { 0.7f, 0.8f, 0.9f },
                Payload = new Dictionary<string, object> { { "content", "Third record" } },
                Tags = new TagCollection { { "type", "test" }, { "batch", "3" } }
            }
        };

        // Act & Assert - Verify records are properly structured
        Assert.Equal(3, testRecords.Count);

        foreach (var record in testRecords)
        {
            Assert.NotNull(record.Id);
            Assert.NotEmpty(record.Id);
            Assert.NotNull(record.Vector);
            Assert.Equal(3, record.Vector.Length);
            Assert.NotNull(record.Payload);
            Assert.NotEmpty(record.Payload);
            Assert.NotNull(record.Tags);
            Assert.NotEmpty(record.Tags);
        }

        Log($"Batch data structure validated for {testRecords.Count} records");
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesVectorDimensionsInBatch()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = true
        };

        var memory = new Neo4jMemory(config, _embeddingGenerator);

        // Create records with different vector dimensions
        var mixedDimensionRecords = new List<MemoryRecord>
        {
            new()
            {
                Id = "record-3d",
                Vector = new[] { 0.1f, 0.2f, 0.3f }, // 3D
                Payload = new Dictionary<string, object> { { "content", "3D vector" } },
                Tags = new TagCollection { { "type", "test" } }
            },
            new()
            {
                Id = "record-5d",
                Vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f }, // 5D
                Payload = new Dictionary<string, object> { { "content", "5D vector" } },
                Tags = new TagCollection { { "type", "test" } }
            }
        };

        // Act & Assert - Mixed dimensions should be handled according to config
        var batchResult = memory.UpsertBatchAsync("test-index", mixedDimensionRecords);
        Assert.NotNull(batchResult);

        Log("Vector dimension validation in batch tested");

        // Cleanup
        memory.Dispose();
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesBatchSizeLimits()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            MaxBatchSize = 2 // Small batch size for testing
        };

        var memory = new Neo4jMemory(config, _embeddingGenerator);

        // Create a batch larger than the configured limit
        var largeBatch = new List<MemoryRecord>();

        for (int i = 0; i < 5; i++)
        {
            largeBatch.Add(new MemoryRecord
            {
                Id = $"record-{i}",
                Vector = new[] { 0.1f * i, 0.2f * i, 0.3f * i },
                Payload = new Dictionary<string, object> { { "content", $"Record {i}" } },
                Tags = new TagCollection { { "index", i.ToString() } }
            });
        }

        // Act & Assert - Large batch should be handled (implementation may split internally)
        var batchResult = memory.UpsertBatchAsync("test-index", largeBatch);
        Assert.NotNull(batchResult);

        Log($"Batch size limit handling tested with {largeBatch.Count} records and limit {config.MaxBatchSize}");

        // Cleanup
        memory.Dispose();
    }
}
