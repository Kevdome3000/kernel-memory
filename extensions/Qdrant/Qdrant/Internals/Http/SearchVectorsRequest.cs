// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class SearchVectorsRequest
{
    private readonly string _collectionName;

    [JsonPropertyName("vector")]
    [JsonConverter(typeof(Embedding.JsonConverter))]
    public Embedding StartingVector { get; set; }

    [JsonPropertyName("filter")]
    public Filter.AndClause Filters { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("with_payload")]
    public bool WithPayload { get; set; }

    [JsonPropertyName("with_vector")]
    public bool WithVector { get; set; }

    [JsonPropertyName("score_threshold")]
    public double ScoreThreshold { get; set; } = -1;


    public static SearchVectorsRequest Create(string collectionName)
    {
        return new SearchVectorsRequest(collectionName);
    }


    public static SearchVectorsRequest Create(string collectionName, int vectorSize)
    {
        return new SearchVectorsRequest(collectionName).SimilarTo(new Embedding(vectorSize));
    }


    public SearchVectorsRequest SimilarTo(Embedding vector)
    {
        StartingVector = vector;
        return this;
    }


    public SearchVectorsRequest HavingExternalId(string externalId)
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(externalId, nameof(externalId), "External ID cannot be empty");
        Filters.AndValue(QdrantConstants.PayloadIdField, externalId);
        return this;
    }


    public SearchVectorsRequest HavingAllTags(IEnumerable<string>? tags)
    {
        if (tags == null) { return this; }

        foreach (var tag in tags)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                Filters.AndValue(QdrantConstants.PayloadTagsField, tag);
            }
        }

        return this;
    }


    public SearchVectorsRequest HavingSomeTags(IEnumerable<IEnumerable<string>?>? tagGroups)
    {
        if (tagGroups == null) { return this; }

        var list = tagGroups.ToList();

        if (list.Count < 2)
        {
            return HavingAllTags(list.FirstOrDefault());
        }

        var orFilter = new Filter.OrClause();

        foreach (var tags in list)
        {
            if (tags == null) { continue; }

            var andFilter = new Filter.AndClause();

            foreach (var tag in tags)
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    andFilter.AndValue(QdrantConstants.PayloadTagsField, tag);
                }
            }

            orFilter.Or(andFilter);
        }

        Filters.And(orFilter);

        return this;
    }


    public SearchVectorsRequest WithScoreThreshold(double scoreThreshold)
    {
        ScoreThreshold = scoreThreshold;
        return this;
    }


    public SearchVectorsRequest IncludePayLoad()
    {
        WithPayload = true;
        return this;
    }


    public SearchVectorsRequest IncludeVectorData(bool withVector)
    {
        WithVector = withVector;
        return this;
    }


    public SearchVectorsRequest FromPosition(int offset)
    {
        Offset = offset;
        return this;
    }


    public SearchVectorsRequest Take(int count)
    {
        Limit = count;
        return this;
    }


    public SearchVectorsRequest TakeFirst()
    {
        return FromPosition(0).Take(1);
    }


    public HttpRequestMessage Build()
    {
        Validate();
        return HttpRequest.CreatePostRequest(
            $"collections/{_collectionName}/points/search",
            this);
    }


    private void Validate()
    {
        ArgumentNullExceptionEx.ThrowIfNull(StartingVector, nameof(StartingVector), "Missing target vector, either provide a vector or vector size");
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(_collectionName, nameof(_collectionName), "The collection name cannot be empty");
        ArgumentOutOfRangeExceptionEx.ThrowIfZeroOrNegative(Limit, nameof(Limit), "The max number of vectors to retrieve must be greater than zero");

        Filters.Validate();
    }


    private SearchVectorsRequest(string collectionName)
    {
        _collectionName = collectionName;
        Filters = new Filter.AndClause();
        WithPayload = false;
        WithVector = false;

        // By default take the closest vector only
        FromPosition(0).TakeFirst();
    }
}
