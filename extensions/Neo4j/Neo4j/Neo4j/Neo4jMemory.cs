using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Neoo4j;
using Neo4j.Driver;

// ReSharper disable InconsistentNaming

// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory.Neo4j;

/// <summary>
///     Basic vector db implementation, designed for tests and demos only.
///     When searching, uses brute force comparing against all stored records.
/// </summary>
// ReSharper disable once InconsistentNaming
[SuppressMessage("Style", "IDE1006:Naming Styles")]
public sealed class Neo4jMemory : IMemoryDb, IMemoryDbUpsertBatch, IDisposable, IAsyncDisposable
{

    private readonly IDriver _driver;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<Neo4jMemory> _log;
    private readonly Neo4jConfig _config;
    private readonly ConcurrentDictionary<string, int> _indexDimensions = new();


    /// <summary>
    ///     Create new instance
    /// </summary>
    /// <param name="config">Neo4j connection settings</param>
    /// <param name="embeddingGenerator">Text embedding generator</param>
    /// <param name="log">Application logger</param>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    public Neo4jMemory(
        Neo4jConfig config,
        ITextEmbeddingGenerator embeddingGenerator,
        ILogger<Neo4jMemory>? log = null,
        ILoggerFactory? loggerFactory = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));

        ILoggerFactory factory = loggerFactory ?? DefaultLogger.Factory;
        _log = log ?? factory.CreateLogger<Neo4jMemory>();

        _driver = Neo4jDriverFactory.BuildDriver(config, factory);

        _log.LogInformation("Neo4jMemory created for {Uri}", config.Uri);

        // Perform feature detection if enabled
        if (config.FeatureDetectionOnStartup)
        {
            Task.Run(async () =>
            {
                try
                {
                    await PerformFeatureDetectionAsync().ConfigureAwait(false);
                }
                catch (Neo4jException ex)
                {
                    _log.LogWarning(ex, "Feature detection failed during startup");
                }
            });
        }
    }


    /// <summary>
    /// Performs feature detection to verify Neo4j capabilities required for vector operations
    /// </summary>
    private async Task PerformFeatureDetectionAsync()
    {
        const string versionQuery = "CALL dbms.components() YIELD name, versions, edition RETURN name, versions[0] as version, edition";

        try
        {
            _log.LogDebug("[DEBUG_LOG] Starting feature detection");

            // Check Neo4j version
            _log.LogDebug("[DEBUG_LOG] Executing version query: {Query}", versionQuery);

            EagerResult<IReadOnlyList<IRecord>>? versionResult = await _driver.ExecutableQuery(versionQuery)
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync()
                .ConfigureAwait(false);

            IRecord? first = versionResult.Result[0];

            string? neo4jVersion = first?["version"]?.As<string>();
            _log.LogInformation("Detected Neo4j version: {Version}", neo4jVersion ?? "Unknown");

            // Check for vector index support
            string proceduresQuery = "SHOW PROCEDURES YIELD name WHERE name CONTAINS 'vector' RETURN name";
            _log.LogDebug("[DEBUG_LOG] Executing procedures query: {Query}", proceduresQuery);

            EagerResult<IReadOnlyList<IRecord>>? proceduresResult = await _driver.ExecutableQuery(proceduresQuery)
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync()
                .ConfigureAwait(false);

            List<string> vectorProcedures = proceduresResult.Result.Select(r => r["name"].As<string>()).ToList();

            bool hasVectorIndexSupport = vectorProcedures.Any(p => p.Contains("db.index.vector", StringComparison.OrdinalIgnoreCase));
            bool hasSetVectorProperty = vectorProcedures.Any(p => p.Contains("db.create.setNodeVectorProperty", StringComparison.OrdinalIgnoreCase));

            if (!hasVectorIndexSupport)
            {
                _log.LogWarning("Neo4j vector index procedures not found. Ensure you're using Neo4j 5.x+ with vector index support enabled");
            }

            if (!hasSetVectorProperty)
            {
                _log.LogWarning("db.create.setNodeVectorProperty procedure not found. Vector operations may fail. Ensure Neo4j version 5.x+ with APOC or native vector support");
            }

            if (hasVectorIndexSupport && hasSetVectorProperty)
            {
                _log.LogInformation("Feature detection completed successfully. All required vector capabilities are available");
            }
            else
            {
                _log.LogWarning("Feature detection found missing capabilities. Please upgrade to Neo4j 5.x+ with vector index support");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Feature detection failed. This may indicate connectivity issues or insufficient permissions");

            throw new Neo4jException("Feature detection failed. This may indicate connectivity issues or insufficient permissions", ex);
        }
    }


    /// <inheritdoc />
    public async Task CreateIndexAsync(string index, int vectorSize, CancellationToken cancellationToken = default)
    {
        string normalizedIndex = NormalizeIndexName(index);
        string indexName = ApplyIndexNamePrefix(normalizedIndex);
        string label = LabelForIndex(normalizedIndex);
        string propertyKey = PropertyKeyForIndex(normalizedIndex);

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Create uniqueness constraint
#pragma warning disable CA1308
            string constraintQuery = $@"
                CREATE CONSTRAINT constraint_{label.ToLowerInvariant()}_id IF NOT EXISTS
                FOR (n:{label}) REQUIRE n.id IS UNIQUE";
#pragma warning restore CA1308

            _log.LogDebug("[DEBUG_LOG] Executing constraint query: {Query}", constraintQuery);

            await _driver.ExecutableQuery(constraintQuery)
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Create vector index
            string indexQuery = $@"
                CREATE VECTOR INDEX `{indexName}` IF NOT EXISTS
                FOR (m:{label}) ON (m.{propertyKey})
                OPTIONS {{
                    indexConfig: {{
                        `vector.dimensions`: $vectorSize,
                        `vector.similarity_function`: 'cosine'
                    }}
                }}";

            _log.LogDebug("[DEBUG_LOG] Executing index query: {Query} with parameters: vectorSize={VectorSize}",
                indexQuery,
                vectorSize);

            await _driver.ExecutableQuery(indexQuery)
                .WithParameters(new { vectorSize })
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Cache vector dimension for validation
            _indexDimensions[normalizedIndex] = vectorSize;

            stopwatch.Stop();
            _log.LogInformation("Created index {IndexName} with vector size {VectorSize} in {ElapsedMs}ms",
                indexName,
                vectorSize,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create index {Index}", index);
            throw new Neo4jException($"Failed to create index '{index}'", ex);
        }
    }


    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            const string query = "SHOW VECTOR INDEXES YIELD name";

            _log.LogDebug("[DEBUG_LOG] Executing get indexes query: {Query}", query);

            EagerResult<IReadOnlyList<IRecord>>? response = await _driver.ExecutableQuery(query)
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            IEnumerable<string> allIndexes = response.Result.Select(x => x["name"].As<string>()).ToList();

            // Filter by IndexNamePrefix if configured
            if (string.IsNullOrEmpty(_config.IndexNamePrefix))
            {
                stopwatch.Stop();
                _log.LogDebug("[DEBUG_LOG] Retrieved {Count} indexes in {ElapsedMs}ms",
                    allIndexes.Count(),
                    stopwatch.ElapsedMilliseconds);
                return allIndexes.ToList();
            }

#pragma warning disable CA1310
            allIndexes = allIndexes.Where(name => name.StartsWith(_config.IndexNamePrefix));
#pragma warning restore CA1310
            // Remove prefix to return normalized names
            allIndexes = allIndexes.Select(name => name[_config.IndexNamePrefix.Length..]);

            List<string> result = allIndexes.ToList();
            stopwatch.Stop();
            _log.LogDebug("[DEBUG_LOG] Retrieved {Count} filtered indexes (prefix: {Prefix}) in {ElapsedMs}ms",
                result.Count,
                _config.IndexNamePrefix,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get indexes from Neo4j database");
            throw new Neo4jException("Failed to retrieve vector indexes from Neo4j", ex);
        }
    }


    /// <inheritdoc />
    public async Task DeleteIndexAsync(string index, CancellationToken cancellationToken = default)
    {
        string normalizedIndex = NormalizeIndexName(index);
        string indexName = ApplyIndexNamePrefix(normalizedIndex);

        try
        {
            await _driver.ExecutableQuery($"DROP INDEX `{indexName}` IF EXISTS")
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Remove from dimension cache
            _indexDimensions.TryRemove(normalizedIndex, out _);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to delete index {Index}", index);
            throw new Neo4jException($"Failed to delete index '{index}'", ex);
        }
    }


    /// <inheritdoc />
    public async Task<string> UpsertAsync(
        string index,
        MemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        string normalizedIndex = NormalizeIndexName(index);
        string label = LabelForIndex(normalizedIndex);
        string propertyKey = PropertyKeyForIndex(normalizedIndex);

        // Validate vector dimensions if cached
        if (_indexDimensions.TryGetValue(normalizedIndex, out int expectedDimensions))
        {
            if (record.Vector.Length != expectedDimensions)
            {
                string message = $"Vector dimension mismatch for index '{index}': expected {expectedDimensions}, got {record.Vector.Length}";

                if (_config.StrictVectorSizeValidation)
                {
                    throw new Neo4jException(message);
                }
                _log.LogWarning(message);
            }
        }

        try
        {
            // Convert tags to the format Neo4j expects
            List<string> flattenedTags = record.Tags.Pairs
                .Select(tag => $"{tag.Key}{Constants.ReservedEqualsChar}{tag.Value}")
                .ToList();

            string payloadJson = JsonSerializer.Serialize(record.Payload, s_jsonOptions);

            // Use ExecutableQuery for single operations (recommended approach)
            EagerResult<IReadOnlyList<IRecord>>? result = await _driver.ExecutableQuery($@"
            MERGE (n:Memory:{label} {{id: $recordId}})
            SET n.payload = $payload,
                n.tags = $tags,
                n.{propertyKey} = $vector
            RETURN n.id as recordId")
                .WithParameters(new
                {
                    recordId = record.Id,
                    payload = payloadJson, // Driver handles Dictionary<string, object> automatically
                    tags = flattenedTags.ToArray(),
                    vector = record.Vector.Data.ToArray()
                })
                .WithConfig(new QueryConfig(database: _config.DatabaseName)) // Always specify database
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.Result.Single()["recordId"].As<string>();
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Failed to upsert record {RecordId} in index {Index}",
                record.Id,
                index);
            throw new Neo4jException($"Failed to upsert record '{record.Id}' in index '{index}'", ex);
        }
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<string> UpsertBatchAsync(
        string index,
        IEnumerable<MemoryRecord> records,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<MemoryRecord> recordList = records.ToList();

        if (recordList.Count == 0)
        {
            yield break;
        }

        string normalizedIndex = NormalizeIndexName(index);
        string label = LabelForIndex(normalizedIndex);
        string propertyKey = PropertyKeyForIndex(normalizedIndex);

        _log.LogDebug("[DEBUG_LOG] Upserting batch of {Count} records to index {Index}",
            recordList.Count,
            index);

        // Validate vector dimensions if cached
        if (_indexDimensions.TryGetValue(normalizedIndex, out int expectedDimensions))
        {
            foreach (string message in from record in recordList
                                       where record.Vector.Length != expectedDimensions
                                       select $"Vector dimension mismatch for index '{index}': expected {expectedDimensions}, got {record.Vector.Length}")
            {
                if (_config.StrictVectorSizeValidation)
                {
                    throw new Neo4jException(message);
                }

                _log.LogWarning(message);
            }
        }

        EagerResult<IReadOnlyList<IRecord>>? result;

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Prepare batch data for UNWIND
            List<object> batchRows = recordList.Select(record =>
                {
                    // Convert tags to the format Neo4j expects
                    List<string> flattenedTags = record.Tags.Pairs
                        .Select(tag => $"{tag.Key}{Constants.ReservedEqualsChar}{tag.Value}")
                        .ToList();

                    string payloadJson = JsonSerializer.Serialize(record.Payload, s_jsonOptions);

                    return new
                    {
                        id = record.Id,
                        payload = payloadJson,
                        tags = flattenedTags.ToArray(),
                        vector = record.Vector.Data.ToArray()
                    };
                })
                .Cast<object>()
                .ToList();

            // Use UNWIND for batch processing as specified in TODO
            string cypher = $@"
                UNWIND $rows AS row
                MERGE (n:Memory:{label} {{id: row.id}})
                SET n.payload = row.payload,
                    n.tags = row.tags
                WITH n, row
                CALL db.create.setNodeVectorProperty(n, $propertyKey, row.vector)
                RETURN n.id AS id";

            _log.LogDebug("[DEBUG_LOG] Executing batch upsert query: {Query} with {Count} rows",
                cypher,
                batchRows.Count);

            result = await _driver.ExecutableQuery(cypher)
                .WithParameters(new
                {
                    rows = batchRows,
                    propertyKey
                })
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            _log.LogInformation("Batch upserted {Count} records to index {Index} in {ElapsedMs}ms",
                recordList.Count,
                index,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Failed to batch upsert {Count} records to index {Index}",
                recordList.Count,
                index);
            throw new Neo4jException($"Failed to batch upsert {recordList.Count} records to index '{index}'", ex);
        }

        // Yield the record IDs outside of try/catch
        foreach (IRecord record in result.Result)
        {
            yield return record["id"].As<string>();
        }
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<(MemoryRecord, double)> GetSimilarListAsync(
        string index,
        string text,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        int limit = 1,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            limit = int.MaxValue;
        }

        string normalizedIndex = NormalizeIndexName(index);
        string indexName = ApplyIndexNamePrefix(normalizedIndex);
        string label = LabelForIndex(normalizedIndex);
        string propertyKey = PropertyKeyForIndex(normalizedIndex);

        EagerResult<IReadOnlyList<IRecord>>? result;

        try
        {
            // Check if the vector index exists; if not, return no results
            var indexCheck = await _driver.ExecutableQuery("SHOW VECTOR INDEXES YIELD name WHERE name = $name RETURN name")
                .WithParameters(new { name = indexName })
                .WithConfig(new QueryConfig(database: _config.DatabaseName, routing: RoutingControl.Readers))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (indexCheck.Result.Count == 0)
            {
                yield break;
            }

            // Generate embedding
            Embedding vector = await _embeddingGenerator.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);

            // Build filter condition (no leading keyword)
            (string condition, Dictionary<string, object> parameters) = BuildFilterCondition(filters, "node");

            // Build projection based on withEmbeddings
            string vectorProjection = withEmbeddings
                ? $", node.{propertyKey} as vector"
                : "";

            // Prepare parameters
            Dictionary<string, object> queryParams = new(parameters)
            {
                ["indexName"] = indexName,
                ["topK"] = limit,
                ["vector"] = vector.Data.ToArray(),
                ["minRelevance"] = minRelevance
            };

            // Integrate filter condition by prefixing a single AND when present
            string filterAndClause = string.IsNullOrEmpty(condition)
                ? string.Empty
                : " AND " + condition;

            string cypher = $@"
            CALL db.index.vector.queryNodes($indexName, $topK, $vector)
            YIELD node, score
            WHERE node:Memory:{label}
            {filterAndClause}
            AND score >= $minRelevance
            RETURN score, node.id as recordId, node.payload as payload, node.tags as tags{vectorProjection}
            ORDER BY score DESC";

            result = await _driver.ExecutableQuery(cypher)
                .WithParameters(queryParams)
                .WithConfig(new QueryConfig(
                    database: _config.DatabaseName,
                    routing: RoutingControl.Readers)) // Use read routing for better performance
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get similar records for index {Index}", index);
            throw new Neo4jException($"Failed to get similar records for index '{index}'", ex);
        }

        foreach (IRecord record in result.Result)
        {
            string? payloadJson = record["payload"].As<string>();
            Dictionary<string, object> payload = string.IsNullOrEmpty(payloadJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson, s_jsonOptions)
                ?? new Dictionary<string, object>();

            MemoryRecord memoryRecord = new()
            {
                Id = record["recordId"].As<string>(),
                Payload = payload,
                Tags = ConvertTagsFromNeo4j(record["tags"].As<List<string>>()), // Now expects List<string>
                Vector = withEmbeddings && record.ContainsKey("vector")
                    ? new Embedding(record["vector"].As<List<float>>().ToArray())
                    : new Embedding()
            };

            double score = record["score"].As<double>();
            yield return (memoryRecord, score);
        }
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<MemoryRecord> GetListAsync(
        string index,
        ICollection<MemoryFilter>? filters = null,
        int limit = 1,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            limit = int.MaxValue;
        }

        string normalizedIndex = NormalizeIndexName(index);
        string label = LabelForIndex(normalizedIndex);
        string propertyKey = PropertyKeyForIndex(normalizedIndex);

        EagerResult<IReadOnlyList<IRecord>>? result;

        try
        {
            // Build filter clause
            (string whereClause, Dictionary<string, object> parameters) = BuildWhereClause(filters);

            // Build projection based on withEmbeddings
            string vectorProjection = withEmbeddings
                ? $", n.{propertyKey} as vector"
                : "";

            // Prepare parameters
            Dictionary<string, object> queryParams = new(parameters)
            {
                ["limit"] = limit
            };

            string cypher = $@"
            MATCH (n:Memory:{label})
            {whereClause}
            RETURN n.id as recordId, n.payload as payload, n.tags as tags{vectorProjection}
            LIMIT $limit";

            result = await _driver.ExecutableQuery(cypher)
                .WithParameters(queryParams)
                .WithConfig(new QueryConfig(
                    database: _config.DatabaseName,
                    routing: RoutingControl.Readers))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get records for index {Index}", index);
            throw new Neo4jException($"Failed to get records for index '{index}'", ex);
        }

        foreach (MemoryRecord? memoryRecord in from record in result.Result
                                               let payloadJson = record["payload"].As<string>()
                                               let payload = string.IsNullOrEmpty(payloadJson)
                                                   ? new Dictionary<string, object>()
                                                   : JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson, s_jsonOptions)
                                                   ?? new Dictionary<string, object>()
                                               select new MemoryRecord
                                               {
                                                   Id = record["recordId"].As<string>(),
                                                   Payload = payload,
                                                   Tags = ConvertTagsFromNeo4j(record["tags"].As<List<string>>()), // Now expects List<string>
                                                   Vector = withEmbeddings && record.ContainsKey("vector")
                                                       ? new Embedding(record["vector"].As<List<float>>().ToArray())
                                                       : new Embedding()
                                               })
        {
            yield return memoryRecord;
        }
    }


    /// <inheritdoc />
    public async Task DeleteAsync(string index, MemoryRecord record, CancellationToken cancellationToken = default)
    {
        string normalizedIndex = NormalizeIndexName(index);
        string label = LabelForIndex(normalizedIndex);

        try
        {
            await _driver.ExecutableQuery($$"""
                                            MATCH (n:Memory:{{label}} {id: $recordId})
                                            DETACH DELETE n
                                            """)
                .WithParameters(new { recordId = record.Id })
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Failed to delete record {RecordId} from index {Index}",
                record.Id,
                index);
            throw new Neo4jException($"Failed to delete record '{record.Id}' from index '{index}'", ex);
        }
    }


    #region private ================================================================================

    // Note: normalize "_" to "-" for consistency with other DBs
    private static readonly Regex s_replaceIndexNameCharsRegex = new(@"[\s|\\|/|.|\-|:]");
    private const string ValidSeparator = "_";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };


    /// <summary>
    /// Normalizes the index name by replacing invalid characters with a valid separator
    /// and converting the name to lowercase.
    /// </summary>
    /// <param name="index">The index name to normalize.</param>
    /// <returns>The normalized index name.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the index name is null or empty.</exception>
    internal static string NormalizeIndexName(string index)
    {
        if (string.IsNullOrWhiteSpace(index))
        {
            throw new ArgumentNullException(nameof(index), "The index name is empty");
        }

#pragma warning disable CA1308
        index = s_replaceIndexNameCharsRegex.Replace(index.Trim().ToLowerInvariant(), ValidSeparator);
#pragma warning restore CA1308

        // Use regex to collapse consecutive underscores
        index = Regex.Replace(index, "_+", "_");

        return index.Trim();
    }


    /// <summary>
    /// Generates a label for the given index by converting it to uppercase
    /// and optionally prefixing it with the configured label prefix.
    /// </summary>
    /// <param name="index">The index name.</param>
    /// <returns>The generated label.</returns>
    internal string LabelForIndex(string index)
    {
        // Replace hyphens with underscores for Neo4j label compatibility
        string safe = index.Replace('-', '_');
        string label = safe.ToUpperInvariant();
        return string.IsNullOrEmpty(_config.LabelPrefix)
            ? label
            : _config.LabelPrefix + label;
    }


    /// <summary>
    /// Generates a property key for the given index by prefixing it with "vec_"
    /// and converting it to lowercase.
    /// </summary>
    /// <param name="index">The index name.</param>
    /// <returns>The generated property key.</returns>
    internal static string PropertyKeyForIndex(string index)
    {
        // Replace hyphens with underscores for Neo4j property key compatibility
        string safe = index.Replace('-', '_');
#pragma warning disable CA1308
        return "vec_" + safe.ToLowerInvariant();
#pragma warning restore CA1308
    }


    /// <summary>
    /// Applies the configured index name prefix to the normalized index name.
    /// </summary>
    /// <param name="normalizedIndex">The normalized index name.</param>
    /// <returns>The index name with the prefix applied.</returns>
    internal string ApplyIndexNamePrefix(string normalizedIndex)
    {
        return string.IsNullOrEmpty(_config.IndexNamePrefix)
            ? normalizedIndex
            : _config.IndexNamePrefix + normalizedIndex;
    }


    /// <summary>
    /// Builds a Cypher filter condition (without leading WHERE/AND) and its associated parameters based on the provided filters.
    /// </summary>
    /// <param name="filters">The collection of memory filters to apply.</param>
    /// <param name="nodeAlias">The alias for the node in the Cypher query (default is "n").</param>
    /// <returns>A tuple containing the condition (no leading keyword) and the parameters dictionary.</returns>
    internal static (string condition, Dictionary<string, object> parameters) BuildFilterCondition(
        ICollection<MemoryFilter>? filters,
        string nodeAlias = "n")
    {
        if (filters == null || filters.Count == 0)
        {
            return (string.Empty, new Dictionary<string, object>());
        }

        Dictionary<string, object> parameters = new();
        List<string> orConditions = [];
        int paramCounter = 0;

        foreach (MemoryFilter filter in filters.Where(f => !f.IsEmpty()))
        {
            List<string> andConditions = [];

            foreach ((string key, List<string?> values) in filter)
            {
                List<string> cleanValues = values.Where(v => !string.IsNullOrEmpty(v)).Cast<string>().ToList();

                if (cleanValues.Count == 0)
                {
                    continue;
                }

                // Create flattened tag patterns to search for (same as Postgres implementation)
                List<string> tagPatterns = cleanValues
                    .Select(value => $"{key}{Constants.ReservedEqualsChar}{value}")
                    .ToList();

                string paramName = $"filterParam{paramCounter++}";
                parameters[paramName] = tagPatterns;

                // Check if any of the flattened tag patterns exist in the tags array
                andConditions.Add($"ANY(tagPattern IN ${paramName} WHERE tagPattern IN {nodeAlias}.tags)");
            }

            if (andConditions.Count > 0)
            {
                orConditions.Add($"({string.Join(" AND ", andConditions)})");
            }
        }

        string condition = orConditions.Count > 0
            ? string.Join(" OR ", orConditions)
            : string.Empty;

        return (condition, parameters);
    }


    /// <summary>
    /// Builds a Cypher WHERE clause and its associated parameters based on the provided filters.
    /// </summary>
    /// <param name="filters">The collection of memory filters to apply.</param>
    /// <param name="nodeAlias">The alias for the node in the Cypher query (default is "n").</param>
    /// <returns>A tuple containing the WHERE clause and the parameters dictionary.</returns>
    internal static (string whereClause, Dictionary<string, object> parameters) BuildWhereClause(
        ICollection<MemoryFilter>? filters,
        string nodeAlias = "n")
    {
        (string condition, Dictionary<string, object> parameters) = BuildFilterCondition(filters, nodeAlias);
        string whereClause = string.IsNullOrEmpty(condition)
            ? string.Empty
            : " WHERE " + condition;

        return (whereClause, parameters);
    }


    /// <summary>
    /// Converts Neo4j tags (a dictionary of string keys and list of string values)
    /// into a TagCollection object.
    /// </summary>
    /// <param name="neo4jTags">The Neo4j tags to convert.</param>
    /// <returns>A TagCollection object containing the converted tags.</returns>
    internal static TagCollection ConvertTagsFromNeo4j(List<string>? neo4jTags)
    {
        TagCollection tags = new();

        if (neo4jTags == null || neo4jTags.Count == 0)
        {
            return tags;
        }

        foreach (string flattenedTag in neo4jTags)
        {
            // Split on the reserved equals character (same as Postgres implementation)
            string[] keyValue = flattenedTag.Split(Constants.ReservedEqualsChar, 2);
            string key = keyValue[0];
            string? value = keyValue.Length == 1
                ? null
                : keyValue[1];

            if (!string.IsNullOrEmpty(key))
            {
                tags.Add(key, value);
            }
        }

        return tags;
    }


    /// <summary>
    /// Checks if the given tags match any of the provided filters.
    /// </summary>
    /// <param name="tags">The tags to check.</param>
    /// <param name="filters">The collection of memory filters to match against.</param>
    /// <returns>True if the tags match any filter, otherwise false.</returns>
    internal static bool TagsMatchFilters(TagCollection tags, ICollection<MemoryFilter>? filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return true;
        }

        // Verify that at least one filter matches (OR logic)
        foreach (MemoryFilter filter in filters)
        {
            bool match = true;

            // Verify that all conditions are met (AND logic)
            foreach (KeyValuePair<string, List<string?>> condition in filter) // Check if the tag name + value is present
            {
                for (int index = 0; match && index < condition.Value.Count; index++)
                {
                    match = match && tags.ContainsKey(condition.Key) && tags[condition.Key].Contains(condition.Value[index]);
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }


    private static string EncodeId(string realId)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(realId);
        return Convert.ToBase64String(bytes).Replace('=', '_');
    }


    private static string DecodeId(string encodedId)
    {
        byte[] bytes = Convert.FromBase64String(encodedId.Replace('_', '='));
        return Encoding.UTF8.GetString(bytes);
    }

    #endregion


    /// <inheritdoc />
    public void Dispose()
    {
        _driver.Dispose();
    }


    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync().ConfigureAwait(false);
    }
}
