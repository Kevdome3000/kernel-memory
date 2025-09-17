using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Search;
// ReSharper disable InconsistentNaming

// ReSharper disable once CheckNamespace
namespace Microsoft.KernelMemory.StructRAG;

#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


[SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase")]
public sealed class StructRAGSearchClient : ISearchClient
{
    private readonly IMemoryDb _memoryDb;
    private readonly ITextGenerator _textGenerator;
    private readonly SearchClientConfig _config;
    private readonly ILogger<StructRAGSearchClient> _log;


    public StructRAGSearchClient(
        IMemoryDb memoryDb,
        ITextGenerator textGenerator,
        SearchClientConfig? config = null,
        ILoggerFactory? loggerFactory = null)
    {
        _memoryDb = memoryDb;
        _textGenerator = textGenerator;
        _log = loggerFactory?.CreateLogger<StructRAGSearchClient>() ?? new NullLogger<StructRAGSearchClient>();

        _config = config ?? new SearchClientConfig();
        _config.Validate();
    }


    public async IAsyncEnumerable<MemoryAnswer> AskStreamingAsync(
        string index,
        string question,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        IContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await AskAsync(index,
            question,
            filters,
            minRelevance,
            context,
            cancellationToken).ConfigureAwait(false);
    }


    public async Task<MemoryAnswer> AskAsync(
        string index,
        string question,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Asking question: {0}", question);

        IEnumerable<(MemoryRecord Record, double Relevance)> records = await GetSimilarRecordsAsync(index,
                question,
                filters,
                minRelevance,
                cancellationToken)
            .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Debug))
        {
            _log.LogDebug("Found {0} relevant memories, maxRelevance: {1}, minRelevance: {2}",
                records.Count(),
                records.MaxBy(c => c.Relevance).Relevance,
                records.MinBy(c => c.Relevance).Relevance);
        }

        if (!records.Any())
        {
            return new MemoryAnswer
            {
                Question = question,
                Result = _config.EmptyAnswer
            };
        }

        IEnumerable<MemoryRecord> effectiveRecords = records.Select(c => c.Record);

        // 1. router
        string route = await RouteAsync(question,
                effectiveRecords,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        _log.LogInformation("Route: {0}", route);

        // 2. structurizer
        (string instruction, string info) = await ConstructAsync(route,
                question,
                effectiveRecords,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Trace))
        {
            _log.LogTrace("Instruction: {0}\nInfo: {1}", instruction, info);
        }

        // 3. utilizer
        IEnumerable<string> subqueries = await DecomposeAsync(question,
                info,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Trace))
        {
            _log.LogTrace("Subqueries: {0}\n{1}", subqueries.Count(), string.Join(Environment.NewLine, subqueries));
        }

        IEnumerable<(string subquery, string subknowledge)> subknowledges = await ExtractAsync(route,
                question,
                info,
                subqueries,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Trace))
        {
            _log.LogTrace("Subknowledges: {0}\n{1}", subknowledges.Count(), string.Join(Environment.NewLine, subknowledges.Select(c => $"Subquery: {c.subquery}\nRetrieval results:\n{c.subknowledge}\n\n")));
        }

        string answer = await MergeAsync(route,
                question,
                subknowledges,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        return new MemoryAnswer
        {
            Question = question,
            Result = answer,
            NoResult = false,
            RelevantSources = records
                .GroupBy(c => c.Record.GetDocumentId())
                .Select(c => new Citation
                {
                    DocumentId = c.Key,
                    FileId = c.First().Record.GetFileId(),
                    Index = index,
                    Link = $"{index}/{c.Key}/{c.First().Record.GetFileId()}",
                    SourceContentType = c.First().Record.GetFileContentType(_log),
                    SourceName = c.First().Record.GetFileName(_log),
                    SourceUrl = c.First().Record.GetWebPageUrl(index),
                    Partitions = c.Select(p => new Citation.Partition
                        {
                            Text = p.Record.GetPartitionText(),
                            LastUpdate = p.Record.GetLastUpdate(),
                            Relevance = (float)p.Relevance,
                            PartitionNumber = p.Record.GetPartitionNumber(),
                            SectionNumber = p.Record.GetSectionNumber(),
                            Tags = p.Record.Tags
                        })
                        .ToList()
                })
                .ToList()
        };
    }


    public async Task<IEnumerable<string>> ListIndexesAsync(CancellationToken cancellationToken = default)
    {
        return await _memoryDb.GetIndexesAsync(cancellationToken)
            .ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(
        string index,
        string query,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        int limit = -1,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) { limit = _config.MaxMatchesCount; }

        SearchResult result = new()
        {
            Query = query,
            Results = []
        };

        if (string.IsNullOrWhiteSpace(query) && (filters == null || filters.Count == 0))
        {
            _log.LogWarning("No query or filters provided");
            return result;
        }

        List<(MemoryRecord memory, double relevance)> list = [];

        if (!string.IsNullOrEmpty(query))
        {
            _log.LogTrace("Fetching relevant memories by similarity, min relevance {0}", minRelevance);
            IAsyncEnumerable<(MemoryRecord, double)> matches = _memoryDb.GetSimilarListAsync(
                index: index,
                text: query,
                filters: filters,
                minRelevance: minRelevance,
                limit: limit,
                withEmbeddings: false,
                cancellationToken: cancellationToken);

            // Memories are sorted by relevance, starting from the most relevant
            await foreach ((MemoryRecord memory, double relevance) in matches.ConfigureAwait(false))
            {
                list.Add((memory, relevance));
            }
        }
        else
        {
            _log.LogTrace("Fetching relevant memories by filtering");
            IAsyncEnumerable<MemoryRecord> matches = _memoryDb.GetListAsync(
                index: index,
                filters: filters,
                limit: limit,
                withEmbeddings: false,
                cancellationToken: cancellationToken);

            await foreach (MemoryRecord memory in matches.ConfigureAwait(false))
            {
                list.Add((memory, float.MinValue));
            }
        }

        // Memories are sorted by relevance, starting from the most relevant
        foreach ((MemoryRecord memory, double relevance) in list)
        {
            // Note: a document can be composed by multiple files
            string documentId = memory.GetDocumentId(_log);

            // Identify the file in case there are multiple files
            string fileId = memory.GetFileId(_log);

            // Note: this is not a URL and perhaps could be dropped. For now it acts as a unique identifier. See also SourceUrl.
            string linkToFile = $"{index}/{documentId}/{fileId}";

            string partitionText = memory.GetPartitionText(_log).Trim();

            if (string.IsNullOrEmpty(partitionText))
            {
                _log.LogError("The document partition is empty, doc: {0}", memory.Id);
                continue;
            }

            // Relevance is `float.MinValue` when search uses only filters and no embeddings (see code above)
            if (relevance > float.MinValue) { _log.LogTrace("Adding result with relevance {0}", relevance); }

            // If the file is already in the list of citations, only add the partition
            Citation? citation = result.Results.FirstOrDefault(x => x.Link == linkToFile);

            if (citation == null)
            {
                citation = new Citation();
                result.Results.Add(citation);
            }

            // Add the partition to the list of citations
            citation.Index = index;
            citation.DocumentId = documentId;
            citation.FileId = fileId;
            citation.Link = linkToFile;
            citation.SourceContentType = memory.GetFileContentType(_log);
            citation.SourceName = memory.GetFileName(_log);
            citation.SourceUrl = memory.GetWebPageUrl(index);

            citation.Partitions.Add(new Citation.Partition
            {
                Text = partitionText,
                Relevance = (float)relevance,
                PartitionNumber = memory.GetPartitionNumber(_log),
                SectionNumber = memory.GetSectionNumber(),
                LastUpdate = memory.GetLastUpdate(),
                Tags = memory.Tags
            });

            // In cases where a buggy storage connector is returning too many records
            if (result.Results.Count >= _config.MaxMatchesCount)
            {
                break;
            }
        }

        if (result.Results.Count == 0)
        {
            _log.LogDebug("No memories found");
        }

        return result;
    }


    private async Task<IEnumerable<(MemoryRecord Record, double Relevance)>> GetSimilarRecordsAsync(
        string index,
        string question,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        CancellationToken cancellationToken = default)
    {
        ConfiguredCancelableAsyncEnumerable<(MemoryRecord, double)> chunks = _memoryDb.GetSimilarListAsync(index,
                question,
                filters,
                minRelevance,
                limit: _config.MaxMatchesCount,
                false,
                cancellationToken)
            .ConfigureAwait(false);

        List<(MemoryRecord record, double relevance)> result = [];

        await foreach ((MemoryRecord, double) chunk in chunks)
        {
            result.Add((chunk.Item1, chunk.Item2));
        }

        return result;
    }


    private async Task<string> RouteAsync(
        string question,
        IEnumerable<MemoryRecord> records,
        IContext? context,
        CancellationToken cancellationToken = default)
    {
        string prompt = GetSKPrompt("StructRAG", "Route")
            .Replace("{{$query}}", question)
            .Replace("{{$titles}}", string.Join(" ", records.DistinctBy(c => c.GetDocumentId()).Select(c => c.GetFileName())));

        StringBuilder text = new();

        await foreach (GeneratedTextContent x in _textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return text.ToString();
    }


    private async Task<(string instruction, string info)> ConstructAsync(
        string route,
        string question,
        IEnumerable<MemoryRecord> records,
        IContext? context,
        CancellationToken cancellationToken)
    {
        string promptName = string.Empty;

        records = records.ToList();

        string chunks = string.Join(Environment.NewLine, records.Select(x => $"{x.GetFileName()}: {x.GetPartitionText()}"));
        string instruction = string.Empty;

        switch (route.ToLowerInvariant())
        {
            case "graph":
                instruction = "Based on the given document, construct a graph where entities are the titles of papers and the relation is 'reference', using the given document title as the head and other paper titles as tails.";
                promptName = "ConstructGraph";
                break;
            case "table":
                instruction = $"Query is {question}, please extract relevant complete tables from the document based on the attributes and keywords mentioned in the Query. Note: retain table titles and source information.";
                promptName = "ConstructTable";
                break;
            case "algorithm":
                instruction = $"Query is {question}, please extract relevant algorithms from the document based on the Query.";
                promptName = "ConstructAlgorithm";
                break;
            case "catalogue":
                instruction = $"Query is {question}, please extract relevant catalogues from the document based on the Query.";
                promptName = "ConstructCatalogue";
                break;
            case "chunk":
                instruction = question;
                return (instruction, chunks);

            default:
                throw new InvalidOperationException();
        }

        string prompt = GetSKPrompt("StructRAG", promptName)
            .Replace("{{$instruction}}", question)
            .Replace("{{$titles}}", string.Join(Environment.NewLine, records.Select(x => x.GetFileName())))
            .Replace("{{$raw_content}}", chunks);

        StringBuilder text = new();

        await foreach (GeneratedTextContent x in _textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return (instruction, text.ToString());
    }


    private async Task<IEnumerable<string>> DecomposeAsync(
        string instruction,
        string info,
        IContext? context,
        CancellationToken cancellationToken = default)
    {
        string prompt = GetSKPrompt("StructRAG", "Decompose")
            .Replace("{{$query}}", instruction)
            .Replace("{{$kb_info}}", info);

        StringBuilder text = new();

        await foreach (GeneratedTextContent x in _textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }


    private async Task<IEnumerable<(string subquery, string subknowledge)>> ExtractAsync(
        string route,
        string question,
        string info,
        IEnumerable<string> subqueries,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        string instruction = string.Empty;

        List<(string subquery, string subknowledge)> subknowledges = [];

        foreach (string subquery in subqueries)
        {
            instruction = route.ToLowerInvariant() switch
            {
                "graph" => $"Instruction:\nAnswer the Query based on the given Document.\n\nQuery:\n{subquery}\n\nDocument:\n{info}\n\nOutput:",
                "table" =>
                    $"Instruction:\nThe following Tables show multiple independent tables built from multiple documents.\nFilter these tables according to the query, retaining only the table information that helps answer the query.\nNote that you need to analyze the attributes and entities mentioned in the query and filter accordingly.\nThe information needed to answer the query must exist in one or several tables, and you need to check these tables one by one.\n\nTables:{info}\n\nQuery:{subquery}\n\nOutput:",
                "algorithm" => $"Instruction: According to the query, filter out information from algorithm descriptions that can help answer the query.\nNote, carefully analyze the entities and relationships mentioned in the query and filter based on this information.\n\nAlgorithms:{info}\n\nQuery:{subquery}\n\nOutput:",
                "catalogue" => $"Instruction: According to the query, filter out information from the catalogue that can help answer the query.\nNote, carefully analyze the entities and relationships mentioned in the query and filter based on this information.\n\nCatalogues:{info}\n\nQuery:{subquery}\n\nOutput:",
                "chunk" => $"Instruction:\nAnswer the Query based on the given Document.\n\nQuery:\n{subquery}\n\nDocument:\n{info}\n\nOutput:",
                _ => throw new InvalidOperationException()
            };

            StringBuilder text = new();

            ConfiguredCancelableAsyncEnumerable<GeneratedTextContent> results = _textGenerator
                .GenerateTextAsync(instruction, GetTextGenerationOptions(context), cancellationToken)
                .ConfigureAwait(false);

            List<string> queryknowledges = [];

            await foreach (GeneratedTextContent result in results)
            {
                text.Append(result);
            }

            subknowledges.Add((subquery, text.ToString()));
        }

        return subknowledges;
    }


    private async Task<string> MergeAsync(
        string chosen,
        string question,
        IEnumerable<(string subquery, string subknowledge)> knowledges,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        string prompt = GetSKPrompt("StructRAG", "Merge")
            .Replace("{{$query}}", question)
            .Replace("{{$subknowledges}}", string.Join(Environment.NewLine, knowledges.Select(c => $"Subquery: {c.subquery}\nRetrieval results:\n{c.subknowledge}\n\n")));

        StringBuilder text = new();

        await foreach (GeneratedTextContent x in _textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return text.ToString();
    }


    private TextGenerationOptions GetTextGenerationOptions(IContext? context)
    {
        int maxTokens = context.GetCustomRagMaxTokensOrDefault(_config.AnswerTokens);
        double temperature = context.GetCustomRagTemperatureOrDefault(_config.Temperature);
        double nucleusSampling = context.GetCustomRagNucleusSamplingOrDefault(_config.TopP);

        return new TextGenerationOptions
        {
            MaxTokens = maxTokens,
            Temperature = temperature,
            NucleusSampling = nucleusSampling,
            PresencePenalty = _config.PresencePenalty,
            FrequencyPenalty = _config.FrequencyPenalty,
            StopSequences = _config.StopSequences,
            TokenSelectionBiases = _config.TokenSelectionBiases
        };
    }


    private static string GetSKPrompt(string pluginName, string functionName)
    {
        Stream? resourceStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"Prompts/{pluginName}/{functionName}.txt");

        using StreamReader reader = new(resourceStream!);
        string text = reader.ReadToEnd();
        return text;
    }
}
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
