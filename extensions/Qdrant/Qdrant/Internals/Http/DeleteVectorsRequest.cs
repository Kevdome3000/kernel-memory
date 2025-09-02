// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class DeleteVectorsRequest
{
    private readonly string _collectionName;

    [JsonPropertyName("points")]
    public List<Guid> Ids { get; set; }


    public static DeleteVectorsRequest DeleteFrom(string collectionName)
    {
        return new DeleteVectorsRequest(collectionName);
    }


    public DeleteVectorsRequest DeleteVector(Guid qdrantPointId)
    {
        ArgumentNullExceptionEx.ThrowIfNull(qdrantPointId, nameof(qdrantPointId), "The point ID is NULL");
        Ids.Add(qdrantPointId);
        return this;
    }


    public DeleteVectorsRequest DeleteRange(IEnumerable<Guid> qdrantPointIds)
    {
        ArgumentNullExceptionEx.ThrowIfNull(qdrantPointIds, nameof(qdrantPointIds), "The collection of points' ID  is NULL");
        Ids.AddRange(qdrantPointIds);
        return this;
    }


    public HttpRequestMessage Build()
    {
        Validate();
        return HttpRequest.CreatePostRequest(
            $"collections/{_collectionName}/points/delete",
            this);
    }


    private DeleteVectorsRequest(string collectionName)
    {
        Ids = [];
        _collectionName = collectionName;
    }


    private void Validate()
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(_collectionName, nameof(_collectionName), "The collection name is empty");
        ArgumentNullExceptionEx.ThrowIfEmpty(Ids, nameof(Ids), "The list of vectors to delete is NULL or empty");
    }
}
