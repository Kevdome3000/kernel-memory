// Copyright (c) Microsoft.All rights reserved.

using System.Runtime.CompilerServices;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.MemoryDb.Elasticsearch.Internals;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.MemoryDb.Elasticsearch;

/// <summary>
/// Elasticsearch connector for Kernel Memory.
/// </summary>
public class ElasticsearchMemory : IMemoryDb
{
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly ElasticsearchConfig _config;
    private readonly ILogger<ElasticsearchMemory> _log;
    private readonly ElasticsearchClient _client;


    /// <summary>
    /// Create a new instance of Elasticsearch KM connector
    /// </summary>
    /// <param name="config">Elasticsearch configuration</param>
    /// <param name="embeddingGenerator">Embedding generator</param>
    /// <param name="client">Elasticsearch client</param>
    /// <param name="loggerFactory">Application logger factory</param>
    public ElasticsearchMemory(
        ElasticsearchConfig config,
        ITextEmbeddingGenerator embeddingGenerator,
        ElasticsearchClient? client = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullExceptionEx.ThrowIfNull(embeddingGenerator, nameof(embeddingGenerator), "The embedding generator is NULL");
        ArgumentNullExceptionEx.ThrowIfNull(config, nameof(config), "The configuration is NULL");

        _embeddingGenerator = embeddingGenerator;
        _config = config;
        _client = client ?? new ElasticsearchClient(_config.ToElasticsearchClientSettings());
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<ElasticsearchMemory>();
    }


    /// <inheritdoc />
    public async Task CreateIndexAsync(
        string index,
        int vectorSize,
        CancellationToken cancellationToken = default)
    {
        index = IndexNameHelper.Convert(index, _config);

        var existsResponse = await _client.Indices.ExistsAsync(index, cancellationToken).ConfigureAwait(false);

        if (existsResponse.Exists)
        {
            _log.LogTrace("Index {Index} already exists.", index);
            return;
        }

        var createIdxResponse = await _client.Indices.CreateAsync(index,
                cfg =>
                {
                    cfg.Settings(setts =>
                    {
                        setts.NumberOfShards(_config.ShardCount);
                        setts.NumberOfReplicas(_config.ReplicaCount);
                    });
                },
                cancellationToken)
            .ConfigureAwait(false);

        //int Dimensions = vectorSize; // TODO: make not hardcoded

        var np = new NestedProperty
        {
            Properties = new Properties
            {
                { ElasticsearchTag.NameField, new KeywordProperty() },
                { ElasticsearchTag.ValueField, new KeywordProperty() }
            }
        };

        var mapResponse = await _client.Indices.PutMappingAsync(index,
                x => x
                    .Properties<ElasticsearchMemoryRecord>(propDesc =>
                    {
                        propDesc.Keyword(x => x.Id);
                        propDesc.Nested(ElasticsearchMemoryRecord.TagsField, np);
                        propDesc.Text(x => x.Payload, pd => pd.Index(false));
                        propDesc.Text(x => x.Content);
                        propDesc.DenseVector(x => x.Vector, d => d.Index().Dims(vectorSize).Similarity(DenseVectorSimilarity.Cosine));

                        _config.ConfigureProperties?.Invoke(propDesc);
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        _log.LogTrace("Index {Index} created.", index);
    }


    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetIndexesAsync(
        CancellationToken cancellationToken = default)
    {
        var resp = await _client.Indices.GetAsync(_config.IndexPrefix + "*", cancellationToken).ConfigureAwait(false);

        var names = resp.Indices
            .Select(x => x.Key.ToString().Replace(_config.IndexPrefix, string.Empty, StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _log.LogTrace("Returned {IndexCount} indices: {Indices}.", names.Count, string.Join(", ", names));

        return names;
    }


    /// <inheritdoc />
    public async Task DeleteIndexAsync(
        string index,
        CancellationToken cancellationToken = default)
    {
        index = IndexNameHelper.Convert(index, _config);

        var delResponse = await _client.Indices.DeleteAsync(
                index,
                cancellationToken)
            .ConfigureAwait(false);

        if (delResponse.IsSuccess())
        {
            _log.LogTrace("Index {Index} deleted.", index);
        }
        else
        {
            _log.LogWarning("Index {Index} delete failed.", index);
        }
    }


    /// <inheritdoc />
    public async Task DeleteAsync(
        string index,
        MemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullExceptionEx.ThrowIfNull(record, nameof(record), "The record is NULL");
        index = IndexNameHelper.Convert(index, _config);

        var delResponse = await _client.DeleteAsync<ElasticsearchMemoryRecord>(
                index,
                record.Id,
                delReq =>
                {
                    delReq.Refresh(Refresh.WaitFor);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (delResponse.IsSuccess())
        {
            _log.LogTrace("Record {RecordId} deleted.", record.Id);
        }
        else
        {
            _log.LogWarning("Record {RecordId} delete failed.", record.Id);
        }
    }


    /// <inheritdoc />
    public async Task<string> UpsertAsync(
        string index,
        MemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        index = IndexNameHelper.Convert(index, _config);

        var memRec = ElasticsearchMemoryRecord.FromMemoryRecord(record);

        var response = await _client.UpdateAsync<ElasticsearchMemoryRecord, ElasticsearchMemoryRecord>(
                index,
                memRec.Id,
                updateReq =>
                {
                    updateReq.Refresh(Refresh.WaitFor);

                    var memRec2 = memRec;
                    updateReq.Doc(memRec2);
                    updateReq.DocAsUpsert();
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccess())
        {
            _log.LogTrace("Record {RecordId} upserted.", memRec.Id);
        }
        else
        {
            _log.LogError("Record {RecordId} upsert failed.", memRec.Id);
        }

        return response.Id;
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
        if (limit < 0)
        {
            limit = 10;
        }

        index = IndexNameHelper.Convert(index, _config);

        _log.LogTrace("Searching for '{Text}' on index '{IndexName}' with filters {Filters}. {MinRelevance} {Limit} {WithEmbeddings}",
            text,
            index,
            filters.ToDebugString(),
            minRelevance,
            limit,
            withEmbeddings);

        Embedding embedding = await _embeddingGenerator.GenerateEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);
        var coll = embedding.Data.ToArray();

        var resp = await _client.SearchAsync<ElasticsearchMemoryRecord>(s =>
                    s.Index(index)
                        .Knn(qd =>
                        {
                            qd.K(limit)
                                .Filter(q => ConvertTagFilters(q, filters))
                                .NumCandidates(limit + 100)
                                .Field(x => x.Vector)
                                .QueryVector(coll);
                        }),
                cancellationToken)
            .ConfigureAwait(false);

        if (resp.HitsMetadata is null || resp.HitsMetadata.Hits is null)
        {
            _log.LogWarning("The search returned a null result. Should retry?");
            yield break;
        }

        foreach (var hit in resp.HitsMetadata.Hits)
        {
            if (hit?.Source == null)
            {
                continue;
            }

            _log.LogTrace("Hit: {HitScore}, {HitId}", hit.Score, hit.Id);
            yield return (hit.Source!.ToMemoryRecord(), hit.Score ?? 0);
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
        _log.LogTrace("Querying index '{IndexName}' with filters {Filters}. {Limit} {WithEmbeddings}",
            index,
            filters.ToDebugString(),
            limit,
            withEmbeddings);

        if (limit < 0)
        {
            limit = 10;
        }

        index = IndexNameHelper.Convert(index, _config);

        // ES has a limit
        if (limit > 10000)
        {
            limit = 10000;
        }

        var resp = await _client.SearchAsync<ElasticsearchMemoryRecord>(s =>
                    s.Index(index)
                        .Size(limit)
                        .Query(qd =>
                        {
                            ConvertTagFilters(qd, filters);
                        }),
                cancellationToken)
            .ConfigureAwait(false);

        if (resp.HitsMetadata is null || resp.HitsMetadata.Hits is null)
        {
            yield break;
        }

        foreach (var hit in resp.Hits)
        {
            if (hit?.Source == null)
            {
                continue;
            }

            _log.LogTrace("Hit: {HitScore}, {HitId}", hit.Score, hit.Id);
            yield return hit.Source!.ToMemoryRecord();
        }
    }


    //private string ConvertIndexName(string index) => ESIndexName.Convert(this._config.IndexPrefix + index);


    private QueryDescriptor<ElasticsearchMemoryRecord> ConvertTagFilters(
        QueryDescriptor<ElasticsearchMemoryRecord> qd,
        ICollection<MemoryFilter>? filters = null)
    {
        if (filters == null || filters.Count == 0)
        {
            qd.MatchAll();
            return qd;
        }

        filters = filters.Where(f => f.Keys.Count > 0)
            .ToList(); // Remove empty filters

        if (filters.Count == 0)
        {
            qd.MatchAll();
            return qd;
        }

        List<Query> super = [];

        foreach (MemoryFilter filter in filters)
        {
            List<Query> thisMust = [];
            thisMust.AddRange((from pair in filter.Pairs
                               let newTagQuery = new TermQuery(ElasticsearchMemoryRecord.TagsName)
                               {
                                   Value = pair.Key,
                                   Field = null
                               }
                               let termQuery = new TermQuery(ElasticsearchMemoryRecord.TagsValue)
                               {
                                   Value = pair.Value ?? string.Empty,
                                   Field = null
                               }
                               select (newTagQuery & (Query)termQuery)
                               into newTagQuery
                               select new NestedQuery
                               {
                                   Path = ElasticsearchMemoryRecord.TagsField,
                                   Query = newTagQuery
                               }).Select(nestedQd => (Query)nestedQd));

            // Each filter is a list of key/value pairs.

            var filterQuery = new BoolQuery();
            filterQuery.Must = thisMust.ToArray();
            //filterQuery.MinimumShouldMatch = 1;

            super.Add(filterQuery);
        }

        qd.Bool(bq => bq.Should(super.ToArray()).MinimumShouldMatch(1));

        // ---------------------

        //qd.Nested(nqd =>
        //{
        //    nqd.Path(ElasticsearchMemoryRecord.TagsField);

        //    nqd.Query(nq =>
        //    {
        //        // Each filter is a tag collection.
        //        foreach (MemoryFilter filter in filters)
        //        {
        //            List<Query> all = new();

        //            // Each tag collection is an element of a List<string, List<string?>>>
        //            foreach (var tagName in filter.Keys)
        //            {
        //                List<string?> tagValues = filter[tagName];
        //                List<FieldValue> terms = tagValues.Select(x => (FieldValue)(x ?? FieldValue.Null))
        //                                                  .ToList();
        //                // ----------------

        //                Query newTagQuery = new TermQuery(ElasticsearchMemoryRecord.Tags_Name) { Value = tagName };
        //                newTagQuery &= new TermsQuery() {
        //                    Field = ElasticsearchMemoryRecord.Tags_Value,
        //                    Terms = new TermsQueryField(terms)
        //                };

        //                all.Add(newTagQuery);
        //            }

        //            nq.Bool(bq => bq.Must(all.ToArray()));
        //        }
        //    });
        //});

        return qd;
    }
}
