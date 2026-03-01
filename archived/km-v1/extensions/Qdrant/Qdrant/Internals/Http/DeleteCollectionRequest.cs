// Copyright (c) Microsoft.All rights reserved.

using System.Net.Http;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class DeleteCollectionRequest
{
    private readonly string _collectionName;


    public static DeleteCollectionRequest Create(string collectionName)
    {
        return new DeleteCollectionRequest(collectionName);
    }


    public HttpRequestMessage Build()
    {
        Validate();
        return HttpRequest.CreateDeleteRequest($"collections/{_collectionName}?timeout=30");
    }


    private DeleteCollectionRequest(string collectionName)
    {
        _collectionName = collectionName;
    }


    private void Validate()
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(_collectionName, nameof(_collectionName), "The collection name is empty");
    }
}
