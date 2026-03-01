// Copyright (c) Microsoft. All rights reserved.
using KernelMemory.Core.Embeddings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KernelMemory.Core.Search;

/// <summary>
/// SQLite-based vector search index implementation.
/// Stores normalized vectors as BLOBs and performs K-NN search using dot product.
/// Optionally supports sqlite-vec extension for accelerated distance calculations.
/// </summary>
public sealed class SqliteVectorIndex : IVectorIndex, IDisposable
{
    private const string TableName = "km_vectors";
    private readonly string _connectionString;
    private readonly int _configuredDimensions;
    private readonly bool _useSqliteVec;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<SqliteVectorIndex> _logger;
    private SqliteConnection? _connection;
    private bool _dimensionsValidated;
    private bool _disposed;
    private bool _sqliteVecAvailable;

    /// <inheritdoc />
    public int VectorDimensions => _configuredDimensions;


    /// <summary>
    /// Initializes a new instance of SqliteVectorIndex.
    /// </summary>
    /// <param name="dbPath">Path to the SQLite database file.</param>
    /// <param name="dimensions">Expected vector dimensions (validated on first use).</param>
    /// <param name="useSqliteVec">Whether to attempt loading sqlite-vec extension.</param>
    /// <param name="embeddingGenerator">The embedding generator to use.</param>
    /// <param name="logger">Logger instance.</param>
    public SqliteVectorIndex(
        string dbPath,
        int dimensions,
        bool useSqliteVec,
        IEmbeddingGenerator embeddingGenerator,
        ILogger<SqliteVectorIndex> logger)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(logger);

        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be positive");
        }

        _connectionString = $"Data Source={dbPath}";
        _configuredDimensions = dimensions;
        _useSqliteVec = useSqliteVec;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }


    /// <summary>
    /// Ensures the database connection is open and tables exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
            return;
        }

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Set synchronous=FULL to ensure writes are immediately persisted to disk
        // This prevents data loss when connections are disposed quickly (CLI scenario)
        using (var pragmaCmd = _connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA synchronous=FULL;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Attempt to load sqlite-vec extension if configured
        if (_useSqliteVec)
        {
            _sqliteVecAvailable = await TryLoadSqliteVecExtensionAsync(cancellationToken).ConfigureAwait(false);

            if (!_sqliteVecAvailable)
            {
                _logger.LogWarning(
                    "sqlite-vec extension not found, using pure BLOB storage. " + "For better performance with large datasets (>100K vectors), install sqlite-vec extension.");
            }
        }

        // Create vectors table if it doesn't exist
        // Schema: content_id (primary key), vector (normalized float32 BLOB), created_at (timestamp)
        var createTableSql = $"""
                              CREATE TABLE IF NOT EXISTS {TableName} (
                                  content_id TEXT PRIMARY KEY,
                                  vector BLOB NOT NULL,
                                  created_at TEXT NOT NULL
                              );
                              """;

        var command = _connection.CreateCommand();

        await using (command.ConfigureAwait(false))
        {
            command.CommandText = createTableSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "SqliteVectorIndex initialized at {ConnectionString}, dimensions: {Dimensions}, sqlite-vec: {SqliteVec}",
            _connectionString,
            _configuredDimensions,
            _sqliteVecAvailable);
    }


    /// <inheritdoc />
    public async Task IndexAsync(string contentId, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentNullException.ThrowIfNull(text);

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        // Generate embedding
        _logger.LogDebug("Generating embedding for content {ContentId}", contentId);
        var result = await _embeddingGenerator.GenerateAsync(text, cancellationToken).ConfigureAwait(false);
        var embedding = result.Vector;

        // Validate dimensions on first use (lazy validation)
        if (!_dimensionsValidated)
        {
            if (embedding.Length != _configuredDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding model returned {embedding.Length} dimensions but config specifies {_configuredDimensions}. " + "Update config dimensions to match model output.");
            }

            _dimensionsValidated = true;
            _logger.LogDebug("Dimensions validated: {Dimensions}", _configuredDimensions);
        }

        // Normalize vector at write time (magnitude = 1)
        var normalizedVector = VectorMath.NormalizeVector(embedding);

        // Serialize to BLOB (float32 array -> bytes)
        var vectorBlob = VectorMath.VectorToBlob(normalizedVector);

        // Remove existing entry first (upsert semantics)
        await RemoveAsync(contentId, cancellationToken).ConfigureAwait(false);

        // Insert new entry
        var insertSql = $"INSERT INTO {TableName}(content_id, vector, created_at) VALUES (@contentId, @vector, @createdAt)";

        var insertCommand = _connection!.CreateCommand();

        await using (insertCommand.ConfigureAwait(false))
        {
            insertCommand.CommandText = insertSql;
            insertCommand.Parameters.AddWithValue("@contentId", contentId);
            insertCommand.Parameters.AddWithValue("@vector", vectorBlob);
            insertCommand.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("o"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("Indexed vector for content {ContentId}, dimensions: {Dimensions}", contentId, embedding.Length);
    }


    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string queryText, int limit = 10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryText);

        if (string.IsNullOrWhiteSpace(queryText))
        {
            return [];
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        // Generate query embedding
        _logger.LogDebug("Generating query embedding for vector search");
        var queryResult = await _embeddingGenerator.GenerateAsync(queryText, cancellationToken).ConfigureAwait(false);
        var queryEmbedding = queryResult.Vector;

        // Validate dimensions
        if (queryEmbedding.Length != _configuredDimensions)
        {
            throw new InvalidOperationException(
                $"Query embedding has {queryEmbedding.Length} dimensions but index expects {_configuredDimensions}");
        }

        // Normalize query vector
        var normalizedQuery = VectorMath.NormalizeVector(queryEmbedding);

        // Retrieve all vectors and compute dot product (linear scan K-NN)
        // For large datasets, sqlite-vec would provide optimized search, but we fall back to C# implementation
        var selectSql = $"SELECT content_id, vector FROM {TableName}";

        var selectCommand = _connection!.CreateCommand();

        await using (selectCommand.ConfigureAwait(false))
        {
            selectCommand.CommandText = selectSql;

            var matches = new List<(string ContentId, double Score)>();

            var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var contentId = reader.GetString(0);
                    var vectorBlob = (byte[])reader.GetValue(1);
                    var storedVector = VectorMath.BlobToVector(vectorBlob);

                    // Compute dot product (for normalized vectors, this equals cosine similarity)
                    var score = VectorMath.DotProduct(normalizedQuery, storedVector);
                    matches.Add((contentId, score));
                }
            }

            // Sort by score descending (highest similarity first) and take top N
            var results = matches
                .OrderByDescending(m => m.Score)
                .Take(limit)
                .Select(m => new VectorMatch { ContentId = m.ContentId, Score = m.Score })
                .ToList();

            _logger.LogDebug("Vector search returned {Count} results from {Total} vectors", results.Count, matches.Count);
            return results;
        }
    }


    /// <inheritdoc />
    public async Task RemoveAsync(string contentId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var deleteSql = $"DELETE FROM {TableName} WHERE content_id = @contentId";

        var deleteCommand = _connection!.CreateCommand();

        await using (deleteCommand.ConfigureAwait(false))
        {
            deleteCommand.CommandText = deleteSql;
            deleteCommand.Parameters.AddWithValue("@contentId", contentId);
            var rowsAffected = await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (rowsAffected > 0)
            {
                _logger.LogDebug("Removed vector for content {ContentId}", contentId);
            }
        }
    }


    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var deleteSql = $"DELETE FROM {TableName}";

        var clearCommand = _connection!.CreateCommand();

        await using (clearCommand.ConfigureAwait(false))
        {
            clearCommand.CommandText = deleteSql;
            await clearCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Cleared all vectors from vector index");
    }


    /// <summary>
    /// Disposes the database connection.
    /// Ensures all pending writes are flushed to disk before closing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Flush any pending writes before closing the connection
        // SQLite needs explicit close to ensure writes are persisted
        if (_connection != null)
        {
            try
            {
                // Execute a checkpoint to flush WAL to disk (if WAL mode is enabled)
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "Failed to checkpoint WAL during vector index disposal");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Failed to checkpoint WAL during vector index disposal - connection in invalid state");
            }

            _connection.Close();
            _connection.Dispose();
            _connection = null;
        }

        _disposed = true;
    }


    /// <summary>
    /// Attempts to load the sqlite-vec extension for accelerated vector operations.
    /// </summary>
    /// <returns>True if extension loaded successfully, false otherwise.</returns>
    private async Task<bool> TryLoadSqliteVecExtensionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // sqlite-vec extension name varies by platform
            // Linux: vec0, Windows: vec0.dll, macOS: vec0.dylib
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT load_extension('vec0')";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("sqlite-vec extension loaded successfully");
            return true;
        }
        catch (SqliteException ex) when (ex.Message.Contains("not authorized") || ex.Message.Contains("cannot open"))
        {
            _logger.LogDebug(ex, "sqlite-vec extension not available: {Message}", ex.Message);
            return false;
        }
        catch (SqliteException ex)
        {
            _logger.LogDebug(ex, "Failed to load sqlite-vec extension: {Message}", ex.Message);
            return false;
        }
    }
}
