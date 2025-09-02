// Copyright (c) Microsoft.All rights reserved.

using System.Net.Http;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class GetCollectionsRequest
{
    private readonly string _collectionName;


    public static GetCollectionsRequest Create(string collectionName)
    {
        return new GetCollectionsRequest(collectionName);
    }


    public HttpRequestMessage Build()
    {
        return HttpRequest.CreateGetRequest($"collections/{_collectionName}");
    }


    private GetCollectionsRequest(string collectionName)
    {
        _collectionName = collectionName;
    }
}
