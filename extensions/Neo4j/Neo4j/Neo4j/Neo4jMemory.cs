using System.Collections.Concurrent;
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
public sealed class Neo4jMemory : IMemoryDb, IDisposable, IAsyncDisposable
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
    public Neo4jMemory(
        Neo4jConfig config,
        ITextEmbeddingGenerator embeddingGenerator,
        ILogger<Neo4jMemory>? log = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _log = log ?? DefaultLogger<Neo4jMemory>.Instance;

        _driver = Neo4jDriverFactory.BuildDriver(config, _log);

        Console.WriteLine($"Neo4jMemory created for {config.Uri}");
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
            // Create uniqueness constraint
#pragma warning disable CA1308
            await _driver.ExecutableQuery($@"
                CREATE CONSTRAINT constraint_{label.ToLowerInvariant()}_id IF NOT EXISTS
                FOR (n:{label}) REQUIRE n.id IS UNIQUE")
#pragma warning restore CA1308
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Create vector index
            await _driver.ExecutableQuery($@"
                CREATE VECTOR INDEX `{indexName}` IF NOT EXISTS
                FOR (m:{label}) ON (m.{propertyKey})
                OPTIONS {{
                    indexConfig: {{
                        `vector.dimensions`: $vectorSize,
                        `vector.similarity_function`: 'cosine'
                    }}
                }}")
                .WithParameters(new { vectorSize })
                .WithConfig(new QueryConfig(database: _config.DatabaseName))
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            // Cache vector dimension for validation
            _indexDimensions[normalizedIndex] = vectorSize;

            _log.LogInformation("Created index {IndexName} with vector size {VectorSize}", indexName, vectorSize);
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
            EagerResult<IReadOnlyList<IRecord>>? response = await _driver.ExecutableQuery("SHOW VECTOR INDEXES YIELD name")
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            IEnumerable<string> allIndexes = response.Result.Select(x => x["name"].As<string>());

            // Filter by IndexNamePrefix if configured
            if (string.IsNullOrEmpty(_config.IndexNamePrefix))
            {
                return allIndexes.ToList();
            }

#pragma warning disable CA1310
            allIndexes = allIndexes.Where(name => name.StartsWith(_config.IndexNamePrefix));
#pragma warning restore CA1310
            // Remove prefix to return normalized names
            allIndexes = allIndexes.Select(name => name[_config.IndexNamePrefix.Length..]);

            return allIndexes.ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to get indexes");
            throw new Neo4jException("Failed to get indexes", ex);
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
            // Generate embedding
            Embedding vector = await _embeddingGenerator.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);

            // Build filter clause
            (string whereClause, Dictionary<string, object> parameters) = BuildWhereClause(filters, "node");

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

            string cypher = $@"
            CALL db.index.vector.queryNodes($indexName, $topK, $vector)
            YIELD node, score
            WHERE node:Memory:{label}
            {whereClause.Replace(" WHERE ", " AND ", StringComparison.Ordinal)}
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
    /// Builds a Cypher WHERE clause and its associated parameters based on the provided filters.
    /// </summary>
    /// <param name="filters">The collection of memory filters to apply.</param>
    /// <param name="nodeAlias">The alias for the node in the Cypher query (default is "n").</param>
    /// <returns>A tuple containing the WHERE clause and the parameters dictionary.</returns>
    internal static (string whereClause, Dictionary<string, object> parameters) BuildWhereClause(
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

        string whereClause = orConditions.Count > 0
            ? " WHERE " + string.Join(" OR ", orConditions)
            : string.Empty;

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
