namespace Microsoft.KernelMemory.GraphRAG.Models;

/// <summary>
/// Represents a relationship between two entities in the graph.
/// This model preserves the relationship extraction logic from GraphRag.Net.
/// </summary>
public class RelationshipModel
{
    /// <summary>
    /// The first entity in the relationship
    /// </summary>
    public string Entity1 { get; set; } = string.Empty;

    /// <summary>
    /// The second entity in the relationship
    /// </summary>
    public string Entity2 { get; set; } = string.Empty;

    /// <summary>
    /// The type or nature of the relationship
    /// </summary>
    public string RelationshipType { get; set; } = string.Empty;

    /// <summary>
    /// Description of the relationship
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Strength or confidence of the relationship (0.0 to 1.0)
    /// </summary>
    public double Strength { get; set; } = 1.0;

    /// <summary>
    /// Source document or context where this relationship was found
    /// </summary>
    public string Source { get; set; } = string.Empty;
}


/// <summary>
/// Represents a community of related entities in the graph.
/// Used for community detection and hierarchical summarization.
/// </summary>
public class CommunityModel
{
    /// <summary>
    /// Unique identifier for the community
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Name or label of the community
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description or summary of the community
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Level in the community hierarchy (0 = leaf level)
    /// </summary>
    public int Level { get; set; } = 0;

    /// <summary>
    /// List of entity IDs that belong to this community
    /// </summary>
    public List<string> EntityIds { get; set; } = [];

    /// <summary>
    /// Parent community ID (null for top-level communities)
    /// </summary>
    public string? ParentCommunityId { get; set; }

    /// <summary>
    /// Child community IDs
    /// </summary>
    public List<string> ChildCommunityIds { get; set; } = [];

    /// <summary>
    /// Size of the community (number of entities)
    /// </summary>
    public int Size => EntityIds.Count;

    /// <summary>
    /// Index/collection this community belongs to
    /// </summary>
    public string Index { get; set; } = string.Empty;
}


/// <summary>
/// Represents a graph structure for community detection algorithms.
/// This preserves the adjacency list structure from GraphRag.Net.
/// </summary>
public class Graph
{
    /// <summary>
    /// Adjacency list representation of the graph
    /// Key: node identifier, Value: list of connected node identifiers
    /// </summary>
    public Dictionary<string, List<string>> AdjacencyList { get; set; } = new();


    /// <summary>
    /// Add a node to the graph
    /// </summary>
    /// <param name="nodeId">The node identifier</param>
    public void AddNode(string nodeId)
    {
        if (!AdjacencyList.ContainsKey(nodeId))
        {
            AdjacencyList[nodeId] = [];
        }
    }


    /// <summary>
    /// Add an edge between two nodes
    /// </summary>
    /// <param name="node1">First node identifier</param>
    /// <param name="node2">Second node identifier</param>
    public void AddEdge(string node1, string node2)
    {
        AddNode(node1);
        AddNode(node2);

        if (!AdjacencyList[node1].Contains(node2))
        {
            AdjacencyList[node1].Add(node2);
        }

        if (!AdjacencyList[node2].Contains(node1))
        {
            AdjacencyList[node2].Add(node1);
        }
    }


    /// <summary>
    /// Get all nodes in the graph
    /// </summary>
    /// <returns>Collection of node identifiers</returns>
    public IEnumerable<string> GetNodes() => AdjacencyList.Keys;


    /// <summary>
    /// Get neighbors of a specific node
    /// </summary>
    /// <param name="nodeId">The node identifier</param>
    /// <returns>Collection of neighbor node identifiers</returns>
    public IEnumerable<string> GetNeighbors(string nodeId)
    {
        return AdjacencyList.TryGetValue(nodeId, out List<string>? neighbors)
            ? neighbors
            : [];
    }
}
