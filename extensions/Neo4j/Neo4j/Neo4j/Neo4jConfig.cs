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
    ///     Optional prefix for index names (e.g., "km_" → "km_myindex")
    /// </summary>
    public string? IndexNamePrefix { get; set; }

    /// <summary>
    ///     Optional prefix for node labels (e.g., "KM_" → "KM_MyIndex")
    /// </summary>
    public string? LabelPrefix { get; set; }

    /// <summary>
    ///     Whether to throw exception on vector dimension mismatch (default: false = warning only)
    /// </summary>
    public bool StrictVectorSizeValidation { get; set; } = false;
}
