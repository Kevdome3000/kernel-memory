// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory.Search;

// ReSharper disable once CheckNamespace - reduce number of "using" statements
namespace Microsoft.KernelMemory;

/// <summary>
/// Memory client to upload files and search for answers, without depending
/// on a web service. By design this class is hardcoded to use
/// <see cref="InProcessPipelineOrchestrator"/>, hence the name "Serverless".
/// The class accesses directly storage, vectors and AI.
/// </summary>
public sealed class MemoryServerless : IKernelMemory
{
    private readonly InProcessPipelineOrchestrator _orchestrator;
    private readonly ISearchClient _searchClient;
    private readonly IContextProvider _contextProvider;
    private readonly string? _defaultIndexName;

    /// <summary>
    /// Synchronous orchestrator used by the serverless memory.
    /// The property is public to allow adding synchronous handlers, e.g.
    /// - memory.Orchestrator.TryAddHandlerAsync(...)
    /// - memory.Orchestrator.AddHandlerAsync(...)
    /// - memory.Orchestrator.AddHandler(...)
    /// - memory.Orchestrator.AddHandler...(...)
    /// </summary>
    public InProcessPipelineOrchestrator Orchestrator => _orchestrator;


    public MemoryServerless(
        InProcessPipelineOrchestrator orchestrator,
        ISearchClient searchClient,
        IContextProvider? contextProvider = null,
        KernelMemoryConfig? config = null)
    {
        _orchestrator = orchestrator ?? throw new ConfigurationException("The orchestrator is NULL");
        _searchClient = searchClient ?? throw new ConfigurationException("The search client is NULL");
        _contextProvider = contextProvider ?? new RequestContextProvider();

        // A non-null config object is required in order to get a non-empty default index name
        config ??= new KernelMemoryConfig();
        _defaultIndexName = config.DefaultIndexName;
    }


    /// <inheritdoc />
    public Task<string> ImportDocumentAsync(
        Document document,
        string? index = null,
        IEnumerable<string>? steps = null,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);
        DocumentUploadRequest uploadRequest = new(document, index, steps);
        return ImportDocumentAsync(uploadRequest, context, cancellationToken);
    }


    /// <inheritdoc />
    public Task<string> ImportDocumentAsync(
        string filePath,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);
        var document = new Document(documentId, tags).AddFile(filePath);
        DocumentUploadRequest uploadRequest = new(document, index, steps);
        return ImportDocumentAsync(uploadRequest, context, cancellationToken);
    }


    /// <inheritdoc />
    public Task<string> ImportDocumentAsync(
        DocumentUploadRequest uploadRequest,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);
        var index = IndexName.CleanName(uploadRequest.Index, _defaultIndexName);
        return _orchestrator.ImportDocumentAsync(index,
            uploadRequest,
            context,
            cancellationToken);
    }


    /// <inheritdoc />
    public Task<string> ImportDocumentAsync(
        Stream content,
        string? fileName = null,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);
        var document = new Document(documentId, tags).AddStream(fileName, content);
        DocumentUploadRequest uploadRequest = new(document, index, steps);
        return ImportDocumentAsync(uploadRequest, context, cancellationToken);
    }


    /// <inheritdoc />
    public async Task<string> ImportTextAsync(
        string text,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);
        var content = new MemoryStream(Encoding.UTF8.GetBytes(text));

        await using (content.ConfigureAwait(false))
        {
            return await ImportDocumentAsync(
                    content,
                    "content.txt",
                    documentId,
                    tags,
                    index,
                    steps,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }


    /// <inheritdoc />
    public async Task<string> ImportWebPageAsync(
        string url,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);
        var uri = new Uri(url);
        Verify.ValidateUrl(uri.AbsoluteUri,
            false,
            false,
            true);

        Stream content = new MemoryStream(Encoding.UTF8.GetBytes(uri.AbsoluteUri));

        await using (content.ConfigureAwait(false))
        {
            return await ImportDocumentAsync(
                    content,
                    "content.url",
                    documentId,
                    tags,
                    index,
                    steps,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }


    /// <inheritdoc />
    public async Task<IEnumerable<IndexDetails>> ListIndexesAsync(CancellationToken cancellationToken = default)
    {
        return from index in await _searchClient.ListIndexesAsync(cancellationToken).ConfigureAwait(false)
               select new IndexDetails { Name = index };
    }


    /// <inheritdoc />
    public Task DeleteIndexAsync(string? index = null, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);
        return _orchestrator.StartIndexDeletionAsync(index, cancellationToken);
    }


    /// <inheritdoc />
    public Task DeleteDocumentAsync(string documentId, string? index = null, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);
        return _orchestrator.StartDocumentDeletionAsync(documentId, index, cancellationToken);
    }


    /// <inheritdoc />
    public async Task<bool> IsDocumentReadyAsync(
        string documentId,
        string? index = null,
        CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);
        return await _orchestrator.IsDocumentReadyAsync(index, documentId, cancellationToken).ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task<DataPipelineStatus?> GetDocumentStatusAsync(
        string documentId,
        string? index = null,
        CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);

        try
        {
            DataPipeline? pipeline = await _orchestrator.ReadPipelineStatusAsync(index, documentId, cancellationToken).ConfigureAwait(false);
            return pipeline?.ToDataPipelineStatus();
        }
        catch (PipelineNotFoundException)
        {
            return null;
        }
    }


    /// <inheritdoc />
    public Task<StreamableFileContent> ExportFileAsync(
        string documentId,
        string fileName,
        string? index = null,
        CancellationToken cancellationToken = default)
    {
        var pipeline = new DataPipeline
        {
            Index = IndexName.CleanName(index, _defaultIndexName),
            DocumentId = documentId
        };
        return _orchestrator.ReadFileAsStreamAsync(pipeline, fileName, cancellationToken);
    }


    /// <inheritdoc />
    public Task<SearchResult> SearchAsync(
        string query,
        string? index = null,
        MemoryFilter? filter = null,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        int limit = -1,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);

        if (filter != null)
        {
            if (filters == null) { filters = []; }

            filters.Add(filter);
        }

        index = IndexName.CleanName(index, _defaultIndexName);
        return _searchClient.SearchAsync(
            index,
            query,
            filters,
            minRelevance,
            limit,
            context,
            cancellationToken);
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<MemoryAnswer> AskStreamingAsync(
        string question,
        string? index = null,
        MemoryFilter? filter = null,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        SearchOptions? options = null,
        IContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _contextProvider.InitContext(context);

        if (filter != null)
        {
            filters ??= [];
            filters.Add(filter);
        }

        index = IndexName.CleanName(index, _defaultIndexName);

        if (options is { Stream: true })
        {
            await foreach (var answer in _searchClient.AskStreamingAsync(
                    index,
                    question,
                    filters,
                    minRelevance,
                    context,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                yield return answer;
            }

            yield break;
        }

        yield return await _searchClient.AskAsync(
                index,
                question,
                filters,
                minRelevance,
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
