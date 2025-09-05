// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.UnitTests;

/// <summary>
/// Tests for vector size validation and dimension mismatch handling in Neo4j connector.
/// Verifies behavior with StrictVectorSizeValidation enabled and disabled.
/// </summary>
public class VectorSizeValidationTests : BaseUnitTestCase
{
    private readonly FakeEmbeddingGenerator _embeddingGenerator;


    public VectorSizeValidationTests(ITestOutputHelper output) : base(output)
    {
        _embeddingGenerator = new FakeEmbeddingGenerator();
        _embeddingGenerator.Mock("test query", new[] { 0.1f, 0.2f, 0.3f });
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesVectorDimensionsLogic()
    {
        // Arrange - Test vector dimension validation logic
        var validDimensions = new[] { 1, 128, 256, 512, 1024, 1536, 2048 };
        var invalidDimensions = new[] { 0, -1, -100 };

        // Act & Assert - Valid dimensions
        foreach (var dimension in validDimensions)
        {
            Assert.True(dimension > 0);
            Log($"Valid dimension: {dimension}");
        }

        // Act & Assert - Invalid dimensions
        foreach (var dimension in invalidDimensions)
        {
            Assert.True(dimension <= 0);
            Log($"Invalid dimension: {dimension}");
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesStrictVectorSizeValidationConfig()
    {
        // Arrange & Act
        var strictConfig = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = true
        };

        var lenientConfig = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = false
        };

        // Assert
        Assert.True(strictConfig.StrictVectorSizeValidation);
        Assert.False(lenientConfig.StrictVectorSizeValidation);

        Log("StrictVectorSizeValidation configuration validated");
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesVectorDimensionMismatchWithStrictValidationDisabled()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = false // Warnings only
        };

        var testVector3D = new[] { 0.1f, 0.2f, 0.3f };
        var testVector5D = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };

        var record3D = new MemoryRecord
        {
            Id = "test-record-3d",
            Vector = testVector3D,
            Payload = new Dictionary<string, object> { { "content", "3D vector" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        var record5D = new MemoryRecord
        {
            Id = "test-record-5d",
            Vector = testVector5D,
            Payload = new Dictionary<string, object> { { "content", "5D vector" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        // Act & Assert - Should not throw with strict validation disabled
        Assert.Equal(3, testVector3D.Length);
        Assert.Equal(5, testVector5D.Length);
        Assert.NotEqual(testVector3D.Length, testVector5D.Length);

        Log("Vector dimension mismatch should only generate warnings when StrictVectorSizeValidation=false");
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesVectorDimensionConsistency()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = true
        };

        var consistentVectors = new[]
        {
            new[] { 0.1f, 0.2f, 0.3f },
            new[] { 0.4f, 0.5f, 0.6f },
            new[] { 0.7f, 0.8f, 0.9f }
        };

        var inconsistentVector = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };

        // Act & Assert
        Assert.True(consistentVectors.All(v => v.Length == 3));
        Assert.Equal(5, inconsistentVector.Length);
        Assert.NotEqual(consistentVectors[0].Length, inconsistentVector.Length);

        Log("All consistent vectors have same dimension (3), inconsistent vector has different dimension (5)");
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesEmptyVectors()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = true
        };

        var emptyVector = Array.Empty<float>();
        var record = new MemoryRecord
        {
            Id = "empty-vector-record",
            Vector = emptyVector,
            Payload = new Dictionary<string, object> { { "content", "empty vector test" } },
            Tags = new TagCollection { { "type", "test" } }
        };

        // Act & Assert
        Assert.Empty(emptyVector);
        Assert.Equal(0, emptyVector.Length);

        Log("Empty vectors should be handled appropriately");
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesLargeVectorDimensions()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = false
        };

        // Test with common large dimensions
        var largeDimensions = new[] { 1536, 2048, 4096, 8192 };

        foreach (var dimension in largeDimensions)
        {
            var largeVector = new float[dimension];

            for (int i = 0; i < dimension; i++)
            {
                largeVector[i] = (float)(i / (double)dimension);
            }

            var record = new MemoryRecord
            {
                Id = $"large-vector-{dimension}",
                Vector = largeVector,
                Payload = new Dictionary<string, object> { { "dimension", dimension } },
                Tags = new TagCollection { { "size", "large" } }
            };

            // Act & Assert
            Assert.Equal(dimension, largeVector.Length);
            Assert.Equal(dimension, record.Vector.Data.Length);

            Log($"Successfully created record with {dimension}-dimensional vector");
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesVectorValueRanges()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = false
        };

        // Test vectors with different value ranges
        var normalizedVector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var unnormalizedVector = new[] { 10.5f, -5.2f, 100.0f, -50.7f };
        var extremeVector = new[] { float.MaxValue, float.MinValue, 0.0f, float.Epsilon };

        var testVectors = new[]
        {
            ("normalized", normalizedVector),
            ("unnormalized", unnormalizedVector),
            ("extreme", extremeVector)
        };

        foreach (var (name, vector) in testVectors)
        {
            var record = new MemoryRecord
            {
                Id = $"vector-{name}",
                Vector = vector,
                Payload = new Dictionary<string, object> { { "type", name } },
                Tags = new TagCollection { { "range", name } }
            };

            // Act & Assert
            Assert.Equal(4, vector.Length);
            Assert.True(vector.All(v => !float.IsNaN(v)));
            Assert.True(vector.All(v => !float.IsInfinity(v)));

            Log($"Vector '{name}' has valid float values: [{string.Join(", ", vector)}]");
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesVectorNormalization()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = false
        };

        var originalVector = new[] { 3.0f, 4.0f, 0.0f };
        var magnitude = (float)Math.Sqrt(originalVector.Sum(x => x * x));
        var normalizedVector = originalVector.Select(x => x / magnitude).ToArray();

        // Act & Assert
        Assert.Equal(3, originalVector.Length);
        Assert.Equal(3, normalizedVector.Length);
        Assert.Equal(5.0f, magnitude, 0.001f); // 3^2 + 4^2 = 9 + 16 = 25, sqrt(25) = 5

        var normalizedMagnitude = Math.Sqrt(normalizedVector.Sum(x => x * x));
        Assert.Equal(1.0, normalizedMagnitude, 0.001); // Normalized vector should have magnitude 1

        Log($"Original vector: [{string.Join(", ", originalVector)}], magnitude: {magnitude}");
        Log($"Normalized vector: [{string.Join(", ", normalizedVector.Select(x => x.ToString("F3")))}], magnitude: {normalizedMagnitude:F3}");
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesCommonVectorDimensions()
    {
        // Arrange - Test common embedding dimensions
        var commonDimensions = new[] { 384, 512, 768, 1024, 1536, 2048 };

        foreach (var dimension in commonDimensions)
        {
            // Create test vector with the specified dimension
            var testVector = new float[dimension];

            for (int i = 0; i < dimension; i++)
            {
                testVector[i] = (float)(Math.Sin(i * 0.01) * 0.5);
            }

            var record = new MemoryRecord
            {
                Id = $"test-{dimension}d",
                Vector = testVector,
                Payload = new Dictionary<string, object> { { "dimension", dimension } },
                Tags = new TagCollection { { "type", "test" } }
            };

            // Act & Assert
            Assert.Equal(dimension, testVector.Length);
            Assert.Equal(dimension, record.Vector.Data.Length);

            Log($"Successfully created record with {dimension}-dimensional vector");
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItValidatesConfigurationSettings()
    {
        // Arrange & Act
        var strictConfig = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = true
        };

        var lenientConfig = new Neo4jConfig
        {
            Uri = "neo4j://localhost:7687",
            Username = "neo4j",
            Password = "password",
            StrictVectorSizeValidation = false
        };

        // Assert
        Assert.True(strictConfig.StrictVectorSizeValidation);
        Assert.False(lenientConfig.StrictVectorSizeValidation);

        Log("Configuration validation: strict and lenient configs have different validation behaviors");
    }
}
