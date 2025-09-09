// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory;

// ReSharper disable once InconsistentNaming
public class Neo4jConfig
{
    /// <summary>
    ///     Uri for connecting to Neo4j.
    ///     Default is "neo4j://localhost:7687"
    /// </summary>
#pragma warning disable CA1056
    public string Uri { get; set; } = "neo4j://localhost:7687";
#pragma warning restore CA1056

    /// <summary>
    ///     Database name to use. Default is "neo4j"
    /// </summary>
    public string DatabaseName { get; set; } = "neo4j";

    /// <summary>
    ///     Username required to connect to Neo4j.
    ///     Default is "neo4j"
    /// </summary>
    public string Username { get; set; } = "neo4j";

    /// <summary>
    ///     Password for authenticating username with Neo4j.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    ///     Whether to use TLS encryption for the connection. Default is false.
    /// </summary>
    public bool UseTls { get; set; } = false;

    /// <summary>
    ///     Trust strategy for TLS connections. Default is "TrustAllCertificates".
    ///     Options: "TrustAllCertificates", "TrustSystemCaSignedCertificates"
    /// </summary>
    public string TrustStrategy { get; set; } = "TrustAllCertificates";

    /// <summary>
    ///     Maximum number of connections in the connection pool. Default is 100.
    /// </summary>
    public int MaxConnectionPoolSize { get; init; } = 100;

    /// <summary>
    ///     Connection timeout in seconds. Default is 30.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; init; } = 30;

    /// <summary>
    ///     Query timeout in seconds. Default is 120.
    /// </summary>
    public int QueryTimeoutSeconds { get; init; } = 120;

    /// <summary>
    ///     Maximum number of retries for transient errors. Default is 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    ///     Base delay in milliseconds for retry backoff. Default is 1000.
    /// </summary>
    public int RetryBaseDelayMs { get; init; } = 1000;

    /// <summary>
    ///     Optional prefix for index names (e.g., "km_" → "km_myindex")
    /// </summary>
    public string? IndexNamePrefix { get; set; }

    /// <summary>
    ///     Optional prefix for node labels (e.g., "KM_" → "KM_MyIndex")
    /// </summary>
    public string? LabelPrefix { get; set; }

    /// <summary>
    ///     Default vector dimensions for indexes when not specified. Optional.
    /// </summary>
    public int? DefaultVectorDimensions { get; init; }

    /// <summary>
    ///     Similarity function to use for vector indexes. Default is "cosine".
    /// </summary>
    public string SimilarityFunction { get; init; } = "cosine";

    /// <summary>
    ///     Whether to throw exception on vector dimension mismatch (default: false = warning only)
    /// </summary>
    public bool StrictVectorSizeValidation { get; set; } = false;

    /// <summary>
    ///     Whether to perform feature detection on startup. Default is true.
    /// </summary>
    public bool FeatureDetectionOnStartup { get; init; } = false;

    /// <summary>
    ///     Maximum batch size for batch upsert operations. Default is 1000.
    /// </summary>
    public int MaxBatchSize { get; init; } = 1000;


    /// <summary>
    ///     Validates the configuration settings and throws ArgumentException if invalid.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Uri))
        {
            throw new ArgumentException("Uri cannot be null or empty", nameof(Uri));
        }

        if (string.IsNullOrWhiteSpace(DatabaseName))
        {
            throw new ArgumentException("DatabaseName cannot be null or empty", nameof(DatabaseName));
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new ArgumentException("Username cannot be null or empty", nameof(Username));
        }

        if (MaxConnectionPoolSize <= 0)
        {
            throw new ArgumentException("MaxConnectionPoolSize must be greater than 0", nameof(MaxConnectionPoolSize));
        }

        if (ConnectionTimeoutSeconds <= 0)
        {
            throw new ArgumentException("ConnectionTimeoutSeconds must be greater than 0", nameof(ConnectionTimeoutSeconds));
        }

        if (QueryTimeoutSeconds <= 0)
        {
            throw new ArgumentException("QueryTimeoutSeconds must be greater than 0", nameof(QueryTimeoutSeconds));
        }

        if (MaxRetries < 0)
        {
            throw new ArgumentException("MaxRetries cannot be negative", nameof(MaxRetries));
        }

        if (RetryBaseDelayMs <= 0)
        {
            throw new ArgumentException("RetryBaseDelayMs must be greater than 0", nameof(RetryBaseDelayMs));
        }

        if (DefaultVectorDimensions.HasValue && DefaultVectorDimensions.Value <= 0)
        {
            throw new ArgumentException("DefaultVectorDimensions must be greater than 0 when specified", nameof(DefaultVectorDimensions));
        }

        string[] validSimilarityFunctions = ["cosine", "euclidean", "dot"];

#pragma warning disable CA1308
        if (!validSimilarityFunctions.Contains(SimilarityFunction.ToLowerInvariant()))
#pragma warning restore CA1308
        {
            throw new ArgumentException($"SimilarityFunction must be one of: {string.Join(", ", validSimilarityFunctions)}", nameof(SimilarityFunction));
        }

        string[] validTrustStrategies = ["TrustAllCertificates", "TrustSystemCaSignedCertificates"];

        if (!validTrustStrategies.Contains(TrustStrategy))
        {
            throw new ArgumentException($"TrustStrategy must be one of: {string.Join(", ", validTrustStrategies)}", nameof(TrustStrategy));
        }

        if (MaxBatchSize <= 0)
        {
            throw new ArgumentException("MaxBatchSize must be greater than 0", nameof(MaxBatchSize));
        }
    }
}
