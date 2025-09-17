namespace Microsoft.KernelMemory.GraphRAG.Models;

/// <summary>
/// Represents a graph structure with nodes and edges for GraphRAG processing.
/// This model preserves the core GraphRAG data schema from GraphRag.Net.
/// </summary>
public class GraphModel
{
    /// <summary>
    /// Collection of nodes in the graph
    /// </summary>
    public List<NodeModel> Nodes { get; set; } = [];

    /// <summary>
    /// Collection of edges (relationships) in the graph
    /// </summary>
    public List<EdgeModel> Edges { get; set; } = [];
}


/// <summary>
/// Represents a node (entity) in the graph
/// </summary>
public class NodeModel
{
    /// <summary>
    /// Unique identifier for the node
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name of the entity
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type/category of the entity
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Description of the entity
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Index/collection this node belongs to
    /// </summary>
    public string Index { get; set; } = string.Empty;
}


/// <summary>
/// Represents an edge (relationship) in the graph
/// </summary>
public class EdgeModel
{
    /// <summary>
    /// Unique identifier for the edge
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Source node identifier
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Target node identifier
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Type of relationship
    /// </summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>
    /// Description of the relationship
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Weight/strength of the relationship
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// Index/collection this edge belongs to
    /// </summary>
    public string Index { get; set; } = string.Empty;
}
