// Copyright (c) Microsoft. All rights reserved.
using Microsoft.Extensions.Logging;
using Moq;

namespace KernelMemory.Core.Tests.Search;

/// <summary>
/// Unit tests for SqliteVectorIndex using mock embedding generator.
/// Tests cover indexing, searching, and removal operations with normalized vectors.
/// </summary>
public sealed class SqliteVectorIndexTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Mock<ILogger<SqliteVectorIndex>> _mockLogger;
    private readonly Mock<IEmbeddingGenerator> _mockEmbeddingGenerator;
    private readonly SqliteVectorIndex _vectorIndex;
    private const int TestDimensions = 4;


    public SqliteVectorIndexTests()
    {
        // Use temp file for SQLite
        _dbPath = Path.Combine(Path.GetTempPath(), $"vector_test_{Guid.NewGuid()}.db");
        _mockLogger = new Mock<ILogger<SqliteVectorIndex>>();
        _mockEmbeddingGenerator = new Mock<IEmbeddingGenerator>();

        // Configure mock to return predictable embeddings
        _mockEmbeddingGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => EmbeddingResult.FromVector(GenerateTestEmbedding(text)));

        _vectorIndex = new SqliteVectorIndex(
            _dbPath,
            TestDimensions,
            false,
            _mockEmbeddingGenerator.Object,
            _mockLogger.Object);
    }


    public void Dispose()
    {
        _vectorIndex.Dispose();

        // Clean up temp file
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        // Clean up WAL files
        var walPath = _dbPath + "-wal";
        var shmPath = _dbPath + "-shm";

        if (File.Exists(walPath))
        {
            File.Delete(walPath);
        }

        if (File.Exists(shmPath))
        {
            File.Delete(shmPath);
        }

        GC.SuppressFinalize(this);
    }


    /// <summary>
    /// Generates a deterministic test embedding based on text hash.
    /// Returns consistent embeddings for the same text.
    /// </summary>
    /// <param name="text"></param>
    private static float[] GenerateTestEmbedding(string text)
    {
        // Simple deterministic embedding based on text hash
        var hash = text.GetHashCode();
        var embedding = new float[TestDimensions];

        for (int i = 0; i < TestDimensions; i++)
        {
            embedding[i] = (hash >> i * 8 & 0xFF) / 255.0f;
        }

        return embedding;
    }


    [Fact]
    public async Task IndexAsync_IndexesContentSuccessfully()
    {
        // Arrange
        const string contentId = "test-id-1";
        const string text = "This is a test document for vector search.";

        // Act
        await _vectorIndex.IndexAsync(contentId, text).ConfigureAwait(false);

        // Assert - Search should find it
        var results = await _vectorIndex.SearchAsync(text).ConfigureAwait(false);
        Assert.Single(results);
        Assert.Equal(contentId, results[0].ContentId);
    }


    [Fact]
    public async Task IndexAsync_ReplacesExistingContent()
    {
        // Arrange
        const string contentId = "test-id-replace";
        await _vectorIndex.IndexAsync(contentId, "original content about cats").ConfigureAwait(false);

        // Act - Replace with new content
        await _vectorIndex.IndexAsync(contentId, "updated content about dogs").ConfigureAwait(false);

        // Assert - Search should only find one result for this ID
        var results = await _vectorIndex.SearchAsync("updated content about dogs").ConfigureAwait(false);
        Assert.Single(results);
        Assert.Equal(contentId, results[0].ContentId);
    }


    [Fact]
    public async Task SearchAsync_ReturnsEmptyForEmptyQuery()
    {
        // Arrange
        await _vectorIndex.IndexAsync("id1", "some content").ConfigureAwait(false);

        // Act
        var results = await _vectorIndex.SearchAsync("").ConfigureAwait(false);

        // Assert
        Assert.Empty(results);
    }


    [Fact]
    public async Task SearchAsync_ReturnsMultipleMatches()
    {
        // Arrange
        await _vectorIndex.IndexAsync("id1", "The quick brown fox jumps").ConfigureAwait(false);
        await _vectorIndex.IndexAsync("id2", "A quick rabbit runs fast").ConfigureAwait(false);
        await _vectorIndex.IndexAsync("id3", "Slow turtle walks slowly").ConfigureAwait(false);

        // Act - Search for something
        var results = await _vectorIndex.SearchAsync("fast animal").ConfigureAwait(false);

        // Assert - Should return all indexed items
        Assert.Equal(3, results.Count);
    }


    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        // Arrange - Create many documents
        for (int i = 0; i < 20; i++)
        {
            await _vectorIndex.IndexAsync($"id{i}", $"Document number {i} with common word test").ConfigureAwait(false);
        }

        // Act
        var results = await _vectorIndex.SearchAsync("test", 5).ConfigureAwait(false);

        // Assert
        Assert.Equal(5, results.Count);
    }


    [Fact]
    public async Task SearchAsync_OrdersByScore()
    {
        // Arrange - Create documents
        await _vectorIndex.IndexAsync("id1", "hello world").ConfigureAwait(false);
        await _vectorIndex.IndexAsync("id2", "goodbye world").ConfigureAwait(false);

        // Act
        var results = await _vectorIndex.SearchAsync("hello world").ConfigureAwait(false);

        // Assert - First result should have exact match (highest score)
        Assert.Equal(2, results.Count);
        Assert.Equal("id1", results[0].ContentId);
        Assert.True(results[0].Score >= results[1].Score, "Results should be ordered by score descending");
    }


    [Fact]
    public async Task RemoveAsync_RemovesIndexedContent()
    {
        // Arrange
        const string contentId = "test-remove";
        await _vectorIndex.IndexAsync(contentId, "content to be removed").ConfigureAwait(false);

        // Verify it exists
        var beforeRemove = await _vectorIndex.SearchAsync("content to be removed").ConfigureAwait(false);
        Assert.Single(beforeRemove);

        // Act
        await _vectorIndex.RemoveAsync(contentId).ConfigureAwait(false);

        // Assert
        var afterRemove = await _vectorIndex.SearchAsync("content to be removed").ConfigureAwait(false);
        Assert.Empty(afterRemove);
    }


    [Fact]
    public async Task RemoveAsync_IsIdempotent()
    {
        // Arrange
        const string contentId = "non-existent-id";

        // Act - Should not throw for non-existent content
        await _vectorIndex.RemoveAsync(contentId).ConfigureAwait(false);
        await _vectorIndex.RemoveAsync(contentId).ConfigureAwait(false);

        // Assert - No exception thrown
        Assert.True(true);
    }


    [Fact]
    public async Task ClearAsync_RemovesAllContent()
    {
        // Arrange
        await _vectorIndex.IndexAsync("id1", "first document").ConfigureAwait(false);
        await _vectorIndex.IndexAsync("id2", "second document").ConfigureAwait(false);
        await _vectorIndex.IndexAsync("id3", "third document").ConfigureAwait(false);

        // Verify content exists
        var beforeClear = await _vectorIndex.SearchAsync("document").ConfigureAwait(false);
        Assert.Equal(3, beforeClear.Count);

        // Act
        await _vectorIndex.ClearAsync().ConfigureAwait(false);

        // Assert
        var afterClear = await _vectorIndex.SearchAsync("document").ConfigureAwait(false);
        Assert.Empty(afterClear);
    }


    [Fact]
    public async Task ScoreProperty_IsInValidRange()
    {
        // Arrange
        await _vectorIndex.IndexAsync("id1", "test content for scoring").ConfigureAwait(false);

        // Act
        var results = await _vectorIndex.SearchAsync("test").ConfigureAwait(false);

        // Assert - Score should be in valid range for normalized dot product
        Assert.Single(results);
        Assert.True(results[0].Score >= -1.0 && results[0].Score <= 1.0, "Score should be in range [-1, 1] for normalized vectors");
    }


    [Fact]
    public async Task VectorDimensions_ReturnsConfiguredValue()
    {
        // Assert
        Assert.Equal(TestDimensions, _vectorIndex.VectorDimensions);
    }


    [Fact]
    public async Task IndexAsync_ValidatesDimensionMismatch()
    {
        // Arrange - Configure mock to return wrong dimensions
        var wrongDimensions = new[] { 1.0f, 2.0f }; // Only 2 dimensions instead of 4
        _mockEmbeddingGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmbeddingResult.FromVector(wrongDimensions));

        // Create new index with mismatch
        var mismatchedIndex = new SqliteVectorIndex(
            Path.Combine(Path.GetTempPath(), $"mismatch_test_{Guid.NewGuid()}.db"),
            TestDimensions,
            false,
            _mockEmbeddingGenerator.Object,
            _mockLogger.Object);

        try
        {
            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mismatchedIndex.IndexAsync("id1", "test content")).ConfigureAwait(false);
            Assert.Contains("dimensions", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            mismatchedIndex.Dispose();
        }
    }


    [Fact]
    public void Constructor_ThrowsForInvalidDimensions()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SqliteVectorIndex(
                _dbPath + "_invalid",
                0,
                false,
                _mockEmbeddingGenerator.Object,
                _mockLogger.Object));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SqliteVectorIndex(
                _dbPath + "_invalid2",
                -1,
                false,
                _mockEmbeddingGenerator.Object,
                _mockLogger.Object));
    }


    [Fact]
    public void Constructor_ThrowsForNullArguments()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SqliteVectorIndex(
                null!,
                TestDimensions,
                false,
                _mockEmbeddingGenerator.Object,
                _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() =>
            new SqliteVectorIndex(
                _dbPath + "_null",
                TestDimensions,
                false,
                null!,
                _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() =>
            new SqliteVectorIndex(
                _dbPath + "_null2",
                TestDimensions,
                false,
                _mockEmbeddingGenerator.Object,
                null!));
    }


    [Fact]
    public async Task IndexAsync_ThrowsForNullContentId()
    {
        // Act & Assert - null throws ArgumentNullException, empty/whitespace throws ArgumentException
        await Assert.ThrowsAsync<ArgumentNullException>(() => _vectorIndex.IndexAsync(null!, "test content")).ConfigureAwait(false);

        await Assert.ThrowsAsync<ArgumentException>(() => _vectorIndex.IndexAsync("", "test content")).ConfigureAwait(false);

        await Assert.ThrowsAsync<ArgumentException>(() => _vectorIndex.IndexAsync("  ", "test content")).ConfigureAwait(false);
    }


    [Fact]
    public async Task IndexAsync_ThrowsForNullText()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _vectorIndex.IndexAsync("id1", null!)).ConfigureAwait(false);
    }


    [Fact]
    public async Task SearchAsync_ThrowsForNullQuery()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _vectorIndex.SearchAsync(null!)).ConfigureAwait(false);
    }
}
