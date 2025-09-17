using Microsoft.KernelMemory.GraphRAG.Models;

namespace Microsoft.KernelMemory.GraphRAG.Services;

/// <summary>
/// Service for detecting communities within a graph structure.
/// Preserves the FastLabelPropagation algorithm from GraphRag.Net with option for Leiden integration.
/// </summary>
public interface ICommunityDetectionService
{
    /// <summary>
    /// Detects communities using Fast Label Propagation Algorithm
    /// </summary>
    /// <param name="graph">The graph to analyze</param>
    /// <param name="iterations">Number of iterations to run</param>
    /// <returns>Dictionary mapping node IDs to community labels</returns>
    Dictionary<string, string> FastLabelPropagationAlgorithm(Graph graph, int iterations = 10);


    /// <summary>
    /// Detects communities using the configured algorithm (FLPA or Leiden)
    /// </summary>
    /// <param name="graph">The graph to analyze</param>
    /// <param name="useLeiden">Whether to use Leiden algorithm (if available)</param>
    /// <returns>Dictionary mapping node IDs to community labels</returns>
    Task<Dictionary<string, string>> DetectCommunitiesAsync(Graph graph, bool useLeiden = false);
}


/// <summary>
/// Implementation of community detection service.
/// Preserves the FastLabelPropagation algorithm from GraphRag.Net.
/// </summary>
public class CommunityDetectionService : ICommunityDetectionService
{

    /// <summary>
    /// Fast Label Propagation Algorithm for community detection.
    /// Preserved from GraphRag.Net Domain/Service/CommunityDetectionService.cs
    /// </summary>
    /// <param name="graph">The graph to analyze</param>
    /// <param name="iterations">Number of iterations to run</param>
    /// <returns>Dictionary mapping node IDs to community labels</returns>
    public Dictionary<string, string> FastLabelPropagationAlgorithm(Graph graph, int iterations = 10)
    {
        // Initialize labels - each node starts with its own label
        Dictionary<string, string> labels = graph.AdjacencyList.Keys.ToDictionary(node => node, node => node);

        for (int iter = 0; iter < iterations; iter++)
        {
            // Shuffle nodes to avoid bias
            List<string> nodes = graph.AdjacencyList.Keys.OrderBy(a => Guid.NewGuid()).ToList();

            foreach (string node in nodes)
            {
                // Count neighbor labels
                Dictionary<string, int> labelCounts = new();

                foreach (string neighbor in graph.AdjacencyList[node])
                {
                    if (!labelCounts.TryGetValue(labels[neighbor], out int value))
                    {
                        value = 0;
                        labelCounts[labels[neighbor]] = value;
                    }
                    labelCounts[labels[neighbor]] = ++value;
                }

                // Skip if no neighbors
                if (labelCounts.Count == 0)
                {
                    continue;
                }

                // Find the label with highest frequency
                int maxCount = labelCounts.Values.Max();
                List<string> bestLabels = labelCounts.Where(x => x.Value == maxCount).Select(x => x.Key).ToList();

                // Pick the lexicographically smallest label in case of tie
                string newLabel = bestLabels.OrderBy(x => x).First();

                // Update label if it changed
                if (labels.TryGetValue(node, out string? currentLabel) && currentLabel != newLabel)
                {
                    labels[node] = newLabel;
                }
            }
        }

        return labels;
    }


    /// <summary>
    /// Detects communities using the configured algorithm
    /// </summary>
    /// <param name="graph">The graph to analyze</param>
    /// <param name="useLeiden">Whether to use Leiden algorithm (if available)</param>
    /// <returns>Dictionary mapping node IDs to community labels</returns>
    public async Task<Dictionary<string, string>> DetectCommunitiesAsync(Graph graph, bool useLeiden = false)
    {
        if (useLeiden)
        {
            // TODO: Integrate with Python Leiden service when available
            // For now, fall back to FLPA
        }

        // Use Fast Label Propagation Algorithm
        return await Task.Run(() => FastLabelPropagationAlgorithm(graph)).ConfigureAwait(false);
    }
}
