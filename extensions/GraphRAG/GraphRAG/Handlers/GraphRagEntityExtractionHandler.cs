using Microsoft.KernelMemory.GraphRAG.Models;
using Microsoft.KernelMemory.GraphRAG.Services;

namespace Microsoft.KernelMemory.GraphRAG.Handlers;

/// <summary>
/// Handler for GraphRAG entity extraction processing.
/// Integrates the preserved EntityExtractionService and TextChunkingService for document processing.
/// </summary>
public class GraphRagEntityExtractionHandler
{
    private readonly IEntityExtractionService _entityExtractionService;
    private readonly ITextChunkingService _textChunkingService;


    public GraphRagEntityExtractionHandler(
        IEntityExtractionService entityExtractionService,
        ITextChunkingService textChunkingService)
    {
        _entityExtractionService = entityExtractionService;
        _textChunkingService = textChunkingService;
    }


    /// <summary>
    /// Processes text content to extract entities and relationships using GraphRAG patterns.
    /// Preserves the core processing logic from GraphRag.Net.
    /// </summary>
    /// <param name="content">The text content to process</param>
    /// <param name="index">The index identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of extracted graphs from the content</returns>
    public async Task<List<GraphModel>> ProcessContentAsync(
        string content,
        string index,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        List<GraphModel> extractedGraphs = [];

        try
        {
            // Create optimized chunks using the preserved chunking algorithm
            List<string> chunks = _textChunkingService.CreateOptimizedChunks(content);

            // Process each chunk for entity extraction
            foreach (string chunk in chunks.TakeWhile(chunk => !cancellationToken.IsCancellationRequested))
            {
                GraphModel graph = await _entityExtractionService.CreateGraphAsync(chunk).ConfigureAwait(false);

                if (graph.Nodes.Count <= 0 && graph.Edges.Count <= 0)
                {
                    continue;
                }

                // Set index for all nodes and edges
                foreach (NodeModel node in graph.Nodes)
                {
                    node.Index = index;
                }

                foreach (EdgeModel edge in graph.Edges)
                {
                    edge.Index = index;
                }

                extractedGraphs.Add(graph);
            }

            return extractedGraphs;
        }
        catch (Exception)
        {
            // Return empty list on error - caller can handle logging
            return [];
        }
    }
}
