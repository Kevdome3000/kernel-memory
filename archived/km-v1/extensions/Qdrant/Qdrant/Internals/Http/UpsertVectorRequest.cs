// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class UpsertVectorRequest<T> where T : DefaultQdrantPayload, new()
{
    internal sealed class BatchRequest
    {
        [JsonPropertyName("ids")]
        public List<Guid> Ids { get; set; } = [];

        [JsonPropertyName("vectors")]
        public List<Embedding> Vectors { get; set; } = [];

        [JsonPropertyName("payloads")]
        public List<T> Payloads { get; set; } = [];
    }


    private readonly string _collectionName;

    [JsonPropertyName("batch")]
    public BatchRequest Batch { get; set; }


    public static UpsertVectorRequest<T> Create(string collectionName)
    {
        return new UpsertVectorRequest<T>(collectionName);
    }


    public UpsertVectorRequest<T> UpsertVector(QdrantPoint<T> vectorRecord)
    {
        Batch.Ids.Add(vectorRecord.Id);
        Batch.Vectors.Add(vectorRecord.Vector);
        Batch.Payloads.Add(vectorRecord.Payload);
        return this;
    }


    public UpsertVectorRequest<T> UpsertRange(IEnumerable<QdrantPoint<T>> vectorRecords)
    {
        foreach (var vectorRecord in vectorRecords)
        {
            UpsertVector(vectorRecord);
        }

        return this;
    }


    public HttpRequestMessage Build()
    {
        return HttpRequest.CreatePutRequest(
            $"collections/{_collectionName}/points?wait=true",
            this);
    }


    private UpsertVectorRequest(string collectionName)
    {
        _collectionName = collectionName;
        Batch = new BatchRequest();
    }
}
