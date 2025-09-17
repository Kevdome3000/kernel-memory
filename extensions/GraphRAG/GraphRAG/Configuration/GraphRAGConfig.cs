namespace Microsoft.KernelMemory.GraphRAG.Configuration;

/// <summary>
/// Configuration for GraphRAG extension.
/// Provides settings for entity extraction, community detection, chunking, and search operations.
/// </summary>
public class GraphRagConfig
{
    /// <summary>
    /// Entity extraction configuration
    /// </summary>
    public EntityExtractionConfig EntityExtraction { get; set; } = new();

    /// <summary>
    /// Community detection configuration
    /// </summary>
    public CommunityDetectionConfig CommunityDetection { get; set; } = new();

    /// <summary>
    /// Text chunking configuration
    /// </summary>
    public TextChunkingConfig TextChunking { get; set; } = new();

    /// <summary>
    /// Search configuration
    /// </summary>
    public SearchConfig Search { get; set; } = new();

    /// <summary>
    /// Storage configuration
    /// </summary>
    public StorageConfig Storage { get; set; } = new();


    /// <summary>
    /// Validates the configuration settings
    /// </summary>
    public void Validate()
    {
        EntityExtraction.Validate();
        CommunityDetection.Validate();
        TextChunking.Validate();
        Search.Validate();
        Storage.Validate();
    }
}


/// <summary>
/// Configuration for entity extraction operations
/// </summary>
public class EntityExtractionConfig
{
    /// <summary>
    /// Maximum number of retry attempts for entity extraction
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Temperature setting for LLM calls (0.0 to 1.0)
    /// </summary>
    public double Temperature { get; set; } = 0.0;

    /// <summary>
    /// Whether to enable structured JSON output for entity extraction
    /// </summary>
    public bool UseStructuredOutput { get; set; } = true;

    /// <summary>
    /// Timeout for entity extraction operations (in seconds)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;


    /// <summary>
    /// Validates the entity extraction configuration
    /// </summary>
    public void Validate()
    {
        if (MaxRetryAttempts < 0)
        {
            throw new ArgumentException("MaxRetryAttempts must be non-negative");
        }

        if (Temperature < 0.0 || Temperature > 1.0)
        {
            throw new ArgumentException("Temperature must be between 0.0 and 1.0");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new ArgumentException("TimeoutSeconds must be positive");
        }
    }
}


/// <summary>
/// Configuration for community detection operations
/// </summary>
public class CommunityDetectionConfig
{
    /// <summary>
    /// Community detection algorithm to use
    /// </summary>
    public CommunityDetectionAlgorithm Algorithm { get; set; } = CommunityDetectionAlgorithm.FastLabelPropagation;

    /// <summary>
    /// Number of iterations for Fast Label Propagation Algorithm
    /// </summary>
    public int FlpaIterations { get; set; } = 10;

    /// <summary>
    /// Whether to use Leiden algorithm when available
    /// </summary>
    public bool PreferLeiden { get; set; } = false;

    /// <summary>
    /// Leiden service endpoint (when using Leiden algorithm)
    /// </summary>
    public string? LeidenServiceEndpoint { get; set; }

    /// <summary>
    /// Timeout for community detection operations (in seconds)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;


    /// <summary>
    /// Validates the community detection configuration
    /// </summary>
    public void Validate()
    {
        if (FlpaIterations <= 0)
        {
            throw new ArgumentException("FlpaIterations must be positive");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new ArgumentException("TimeoutSeconds must be positive");
        }

        if (PreferLeiden && string.IsNullOrWhiteSpace(LeidenServiceEndpoint))
        {
            throw new ArgumentException("LeidenServiceEndpoint is required when PreferLeiden is true");
        }
    }
}


/// <summary>
/// Community detection algorithms
/// </summary>
public enum CommunityDetectionAlgorithm
{
    /// <summary>
    /// Fast Label Propagation Algorithm (preserved from GraphRag.Net)
    /// </summary>
    FastLabelPropagation,

    /// <summary>
    /// Leiden algorithm (via Python service)
    /// </summary>
    Leiden,

    /// <summary>
    /// Automatic selection based on availability
    /// </summary>
    Auto
}


/// <summary>
/// Configuration for text chunking operations
/// </summary>
public class TextChunkingConfig
{
    /// <summary>
    /// Maximum number of paragraphs per chunk
    /// </summary>
    public int MaxChunkSize { get; set; } = 3;

    /// <summary>
    /// Number of paragraphs to overlap between chunks
    /// </summary>
    public int OverlapSize { get; set; } = 1;

    /// <summary>
    /// Maximum tokens per line chunk
    /// </summary>
    public int LinesTokenLimit { get; set; } = 100;

    /// <summary>
    /// Maximum tokens per paragraph chunk
    /// </summary>
    public int ParagraphsTokenLimit { get; set; } = 1000;


    /// <summary>
    /// Validates the text chunking configuration
    /// </summary>
    public void Validate()
    {
        if (MaxChunkSize <= 0)
        {
            throw new ArgumentException("MaxChunkSize must be positive");
        }

        if (OverlapSize < 0)
        {
            throw new ArgumentException("OverlapSize must be non-negative");
        }

        if (OverlapSize >= MaxChunkSize)
        {
            throw new ArgumentException("OverlapSize must be less than MaxChunkSize");
        }

        if (LinesTokenLimit <= 0)
        {
            throw new ArgumentException("LinesTokenLimit must be positive");
        }

        if (ParagraphsTokenLimit <= 0)
        {
            throw new ArgumentException("ParagraphsTokenLimit must be positive");
        }
    }
}


/// <summary>
/// Configuration for search operations
/// </summary>
public class SearchConfig
{
    /// <summary>
    /// Default search method to use
    /// </summary>
    public SearchMethod DefaultSearchMethod { get; set; } = SearchMethod.Hybrid;

    /// <summary>
    /// Confidence threshold for search results (0.0 to 1.0)
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.5;

    /// <summary>
    /// Maximum number of search results to return
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Timeout for search operations (in seconds)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;


    /// <summary>
    /// Validates the search configuration
    /// </summary>
    public void Validate()
    {
        if (ConfidenceThreshold < 0.0 || ConfidenceThreshold > 1.0)
        {
            throw new ArgumentException("ConfidenceThreshold must be between 0.0 and 1.0");
        }

        if (MaxResults <= 0)
        {
            throw new ArgumentException("MaxResults must be positive");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new ArgumentException("TimeoutSeconds must be positive");
        }
    }
}


/// <summary>
/// Search methods available in GraphRAG
/// </summary>
public enum SearchMethod
{
    /// <summary>
    /// Local search using entity-relationship context
    /// </summary>
    Local,

    /// <summary>
    /// Global search using community-based approach
    /// </summary>
    Global,

    /// <summary>
    /// Hybrid search combining local and global approaches
    /// </summary>
    Hybrid
}


/// <summary>
/// Configuration for storage operations
/// </summary>
public class StorageConfig
{
    /// <summary>
    /// Whether to enable graph data persistence
    /// </summary>
    public bool EnablePersistence { get; set; } = true;

    /// <summary>
    /// Storage format for graph data
    /// </summary>
    public StorageFormat Format { get; set; } = StorageFormat.Json;

    /// <summary>
    /// Whether to compress stored graph data
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Batch size for bulk storage operations
    /// </summary>
    public int BatchSize { get; set; } = 100;


    /// <summary>
    /// Validates the storage configuration
    /// </summary>
    public void Validate()
    {
        if (BatchSize <= 0)
        {
            throw new ArgumentException("BatchSize must be positive");
        }
    }
}


/// <summary>
/// Storage formats for graph data
/// </summary>
public enum StorageFormat
{
    /// <summary>
    /// JSON format
    /// </summary>
    Json,

    /// <summary>
    /// Parquet format (compatible with Microsoft GraphRAG)
    /// </summary>
    Parquet,

    /// <summary>
    /// Binary format
    /// </summary>
    Binary
}
