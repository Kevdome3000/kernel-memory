namespace Microsoft.KernelMemory.GraphRAG.Services;

/// <summary>
/// Service for text chunking optimized for GraphRAG processing.
/// Preserves the overlapping chunking strategy from GraphRag.Net GraphService.
/// </summary>
public interface ITextChunkingService
{
    /// <summary>
    /// Creates overlapping text chunks to maintain relationship information across boundaries
    /// </summary>
    /// <param name="paragraphs">List of paragraphs to chunk</param>
    /// <param name="maxChunkSize">Maximum number of paragraphs per chunk</param>
    /// <param name="overlapSize">Number of paragraphs to overlap between chunks</param>
    /// <returns>List of overlapping text chunks</returns>
    List<string> CreateOverlappingChunks(List<string> paragraphs, int maxChunkSize = 3, int overlapSize = 1);


    /// <summary>
    /// Splits text into optimized chunks for GraphRAG processing
    /// </summary>
    /// <param name="text">The input text to chunk</param>
    /// <param name="linesTokenLimit">Maximum tokens per line chunk</param>
    /// <param name="paragraphsTokenLimit">Maximum tokens per paragraph chunk</param>
    /// <returns>List of optimized text chunks</returns>
    List<string> CreateOptimizedChunks(string text, int linesTokenLimit = 100, int paragraphsTokenLimit = 1000);
}


/// <summary>
/// Implementation of text chunking service.
/// Preserves the overlapping chunking strategy from GraphRag.Net GraphService.CreateOverlappingChunks
/// </summary>
public class TextChunkingService : ITextChunkingService
{
    /// <summary>
    /// Creates overlapping text chunks to maintain relationship information across boundaries.
    /// Preserved from GraphRag.Net Domain/Service/GraphService.cs (lines 125-167)
    /// </summary>
    /// <param name="paragraphs">List of paragraphs to chunk</param>
    /// <param name="maxChunkSize">Maximum number of paragraphs per chunk (default: 3)</param>
    /// <param name="overlapSize">Number of paragraphs to overlap between chunks (default: 1)</param>
    /// <returns>List of overlapping text chunks</returns>
    public List<string> CreateOverlappingChunks(List<string> paragraphs, int maxChunkSize = 3, int overlapSize = 1)
    {
        List<string> chunks = [];

        if (paragraphs.Count <= maxChunkSize)
        {
            // If the number of paragraphs is not large, use directly as a single chunk
            chunks.Add(string.Join("\n\n", paragraphs));
        }
        else
        {
            // Create overlapping text chunks
            for (int i = 0; i < paragraphs.Count; i += maxChunkSize - overlapSize)
            {
                List<string> chunkParagraphs = paragraphs
                    .Skip(i)
                    .Take(maxChunkSize)
                    .ToList();

                if (chunkParagraphs.Count > 0)
                {
                    string chunk = string.Join("\n\n", chunkParagraphs);

                    // Avoid duplicate chunks
                    if (!chunks.Contains(chunk))
                    {
                        chunks.Add(chunk);
                    }
                }

                // If the remaining paragraphs are less than one complete chunk, exit the loop
                if (i + maxChunkSize >= paragraphs.Count)
                {
                    break;
                }
            }
        }

        return chunks;
    }


    /// <summary>
    /// Splits text into optimized chunks for GraphRAG processing.
    /// Combines line and paragraph splitting with overlapping strategy.
    /// </summary>
    /// <param name="text">The input text to chunk</param>
    /// <param name="linesTokenLimit">Maximum tokens per line chunk (default: 100)</param>
    /// <param name="paragraphsTokenLimit">Maximum tokens per paragraph chunk (default: 1000)</param>
    /// <returns>List of optimized text chunks</returns>
    public List<string> CreateOptimizedChunks(string text, int linesTokenLimit = 100, int paragraphsTokenLimit = 1000)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // Split into lines first (this would integrate with KernelMemory's TextChunker)
        List<string> lines = SplitIntoLines(text, linesTokenLimit);

        // Then split lines into paragraphs
        List<string> paragraphs = SplitIntoParagraphs(lines, paragraphsTokenLimit);

        // Finally create overlapping chunks to maintain relationship information
        return CreateOverlappingChunks(paragraphs);
    }


    /// <summary>
    /// Splits text into lines with token limit.
    /// This would integrate with KernelMemory's TextChunker.SplitPlainTextLines
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="tokenLimit">Token limit per line</param>
    /// <returns>List of text lines</returns>
    private static List<string> SplitIntoLines(string text, int tokenLimit)
    {
        // TODO: Integrate with KernelMemory's TextChunker.SplitPlainTextLines
        // For now, simple split by lines
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }


    /// <summary>
    /// Splits lines into paragraphs with token limit.
    /// This would integrate with KernelMemory's TextChunker.SplitPlainTextParagraphs
    /// </summary>
    /// <param name="lines">Input lines</param>
    /// <param name="tokenLimit">Token limit per paragraph</param>
    /// <returns>List of paragraphs</returns>
    private static List<string> SplitIntoParagraphs(List<string> lines, int tokenLimit)
    {
        // TODO: Integrate with KernelMemory's TextChunker.SplitPlainTextParagraphs
        // For now, simple grouping of lines into paragraphs
        List<string> paragraphs = [];
        List<string> currentParagraph = [];
        int currentTokenCount = 0;

        foreach (string line in lines)
        {
            int lineTokens = EstimateTokenCount(line);

            if (currentTokenCount + lineTokens > tokenLimit && currentParagraph.Count > 0)
            {
                paragraphs.Add(string.Join("\n", currentParagraph));
                currentParagraph = [];
                currentTokenCount = 0;
            }

            currentParagraph.Add(line);
            currentTokenCount += lineTokens;
        }

        if (currentParagraph.Count > 0)
        {
            paragraphs.Add(string.Join("\n", currentParagraph));
        }

        return paragraphs;
    }


    /// <summary>
    /// Estimates token count for a text string.
    /// Simple approximation - would be replaced with proper tokenizer.
    /// </summary>
    /// <param name="text">Input text</param>
    /// <returns>Estimated token count</returns>
    private static int EstimateTokenCount(string text)
    {
        // Simple approximation: ~4 characters per token
        return (text?.Length ?? 0) / 4;
    }
}
