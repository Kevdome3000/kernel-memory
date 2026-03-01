// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.MemoryDb.Qdrant.Internals;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant;

/// <summary>
/// Qdrant connector for Kernel Memory
/// TODO:
/// * allow using more Qdrant specific filtering logic
/// </summary>
[Experimental("KMEXP03")]
public sealed class QdrantMemory : IMemoryDb, IMemoryDbUpsertBatch
{
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly QdrantClient<DefaultQdrantPayload> _qdrantClient;
    private readonly ILogger<QdrantMemory> _log;


    /// <summary>
    /// Create new instance
    /// </summary>
    /// <param name="config">Qdrant connector configuration</param>
    /// <param name="embeddingGenerator">Text embedding generator</param>
    /// <param name="loggerFactory">Application logger factory</param>
    public QdrantMemory(
        QdrantConfig config,
        ITextEmbeddingGenerator embeddingGenerator,
        ILoggerFactory? loggerFactory = null)
    {
        _embeddingGenerator = embeddingGenerator;

        if (_embeddingGenerator == null)
        {
            throw new QdrantException("Embedding generator not configured");
        }

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<QdrantMemory>();
        _qdrantClient = new QdrantClient<DefaultQdrantPayload>(config.Endpoint, config.APIKey, loggerFactory: loggerFactory);
    }


    /// <inheritdoc />
    public Task CreateIndexAsync(
        string index,
        int vectorSize,
        CancellationToken cancellationToken = default)
    {
        index = NormalizeIndexName(index);
        return _qdrantClient.CreateCollectionAsync(index, vectorSize, cancellationToken);
    }


    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {
        return await _qdrantClient
            .GetCollectionsAsync(cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }


    /// <inheritdoc />
    public Task DeleteIndexAsync(
        string index,
        CancellationToken cancellationToken = default)
    {
        try
        {
            index = NormalizeIndexName(index);
            return _qdrantClient.DeleteCollectionAsync(index, cancellationToken);
        }
        catch (IndexNotFoundException)
        {
            _log.LogInformation("Index not found, nothing to delete");
        }

        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public async Task<string> UpsertAsync(
        string index,
        MemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        var result = UpsertBatchAsync(index, [record], cancellationToken);
        var id = await result.SingleAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<string> UpsertBatchAsync(string index, IEnumerable<MemoryRecord> records, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        index = NormalizeIndexName(index);

        // Call ToList to avoid multiple enumerations (CA1851: Possible multiple enumerations of 'IEnumerable' collection. Consider using an implementation that avoids multiple enumerations).
        var localRecords = records.ToList();

        var qdrantPoints = new List<QdrantPoint<DefaultQdrantPayload>>();

        foreach (var record in localRecords)
        {
            QdrantPoint<DefaultQdrantPayload> qdrantPoint;

            if (string.IsNullOrEmpty(record.Id))
            {
                record.Id = Guid.NewGuid().ToString("N");
                qdrantPoint = QdrantPoint<DefaultQdrantPayload>.FromMemoryRecord(record);
                qdrantPoint.Id = Guid.NewGuid();

                _log.LogTrace("Generate new Qdrant point ID {0} and record ID {1}", qdrantPoint.Id, record.Id);
            }
            else
            {
                qdrantPoint = QdrantPoint<DefaultQdrantPayload>.FromMemoryRecord(record);
                QdrantPoint<DefaultQdrantPayload>? existingPoint = await _qdrantClient
                    .GetVectorByPayloadIdAsync(index, record.Id, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (existingPoint == null)
                {
                    qdrantPoint.Id = Guid.NewGuid();
                    _log.LogTrace("No record with ID {0} found, generated a new point ID {1}", record.Id, qdrantPoint.Id);
                }
                else
                {
                    qdrantPoint.Id = existingPoint.Id;
                    _log.LogTrace("Point ID {0} found, updating...", qdrantPoint.Id);
                }
            }

            qdrantPoints.Add(qdrantPoint);
        }

        await _qdrantClient.UpsertVectorsAsync(index, qdrantPoints, cancellationToken).ConfigureAwait(false);

        foreach (var record in localRecords)
        {
            yield return record.Id;
        }
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<(MemoryRecord, double)> GetSimilarListAsync(
        string index,
        string text,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        int limit = 1,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        index = NormalizeIndexName(index);

        if (limit <= 0) { limit = int.MaxValue; }

        // Remove empty filters
        filters = filters?.Where(f => !f.IsEmpty()).ToList();

        var requiredTags = new List<IEnumerable<string>>();

        if (filters is { Count: > 0 })
        {
            requiredTags.AddRange(filters.Select(filter => filter.GetFilters().Select(x => $"{x.Key}{Constants.ReservedEqualsChar}{x.Value}")));
        }

        Embedding textEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);

        List<(QdrantPoint<DefaultQdrantPayload>, double)> results;

        try
        {
            results = await _qdrantClient.GetSimilarListAsync(
                    index,
                    textEmbedding,
                    minRelevance,
                    requiredTags: requiredTags,
                    limit: limit,
                    withVectors: withEmbeddings,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IndexNotFoundException e)
        {
            _log.LogWarning(e, "Index not found");
            // Nothing to return
            yield break;
        }

        foreach (var point in results)
        {
            yield return (point.Item1.ToMemoryRecord(), point.Item2);
        }
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<MemoryRecord> GetListAsync(
        string index,
        ICollection<MemoryFilter>? filters = null,
        int limit = 1,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        index = NormalizeIndexName(index);

        if (limit <= 0) { limit = int.MaxValue; }

        // Remove empty filters
        filters = filters?.Where(f => !f.IsEmpty()).ToList();

        var requiredTags = new List<IEnumerable<string>>();

        if (filters is { Count: > 0 })
        {
            requiredTags.AddRange(filters.Select(filter => filter.GetFilters().Select(x => $"{x.Key}{Constants.ReservedEqualsChar}{x.Value}")));
        }

        List<QdrantPoint<DefaultQdrantPayload>> results;

        try
        {
            results = await _qdrantClient.GetListAsync(
                    index,
                    requiredTags,
                    0,
                    limit,
                    withEmbeddings,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IndexNotFoundException e)
        {
            _log.LogWarning(e, "Index not found");
            // Nothing to return
            yield break;
        }

        foreach (var point in results)
        {
            yield return point.ToMemoryRecord();
        }
    }


    /// <inheritdoc />
    public async Task DeleteAsync(
        string index,
        MemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        index = NormalizeIndexName(index);

        try
        {
            QdrantPoint<DefaultQdrantPayload>? existingPoint = await _qdrantClient
                .GetVectorByPayloadIdAsync(index, record.Id, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (existingPoint == null)
            {
                _log.LogTrace("No record with ID {0} found, nothing to delete", record.Id);
                return;
            }

            _log.LogTrace("Point ID {0} found, deleting...", existingPoint.Id);
            await _qdrantClient.DeleteVectorsAsync(index, [existingPoint.Id], cancellationToken).ConfigureAwait(false);
        }
        catch (IndexNotFoundException e)
        {
            _log.LogInformation(e, "Index not found, nothing to delete");
        }
    }


    #region private ================================================================================

    // Note: "_" is allowed in Qdrant, but we normalize it to "-" for consistency with other DBs
    private static readonly Regex s_replaceIndexNameCharsRegex = new(@"[\s|\\|/|.|_|:]");
    private const string ValidSeparator = "-";


    private static string NormalizeIndexName(string index)
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(index, nameof(index), "The index name is empty");
        index = s_replaceIndexNameCharsRegex.Replace(index.Trim().ToLowerInvariant(), ValidSeparator);

        return index.Trim();
    }

    #endregion


}
