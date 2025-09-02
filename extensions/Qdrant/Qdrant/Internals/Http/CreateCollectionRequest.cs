// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.MemoryDb.Qdrant.Internals.Http;

internal sealed class CreateCollectionRequest
{
    internal sealed class VectorSettings
    {
        private readonly QdrantDistanceType _distanceType;

        [JsonPropertyName("size")]
        public int? Size { get; set; }

        [JsonPropertyName("distance")]
        public string DistanceAsString => _distanceType switch
        {
            QdrantDistanceType.Cosine => "Cosine",
            QdrantDistanceType.DotProduct => "DotProduct",
            QdrantDistanceType.Euclidean => "Euclidean",
            QdrantDistanceType.Manhattan => "Manhattan",
            _ => throw new NotSupportedException($"Distance type {Enum.GetName(_distanceType)} not supported")
        };


        public VectorSettings(int vectorSize, QdrantDistanceType distanceType)
        {
            Size = vectorSize;
            _distanceType = distanceType;
        }


        internal void Validate()
        {
            ArgumentNullExceptionEx.ThrowIfNull(Size, nameof(Size), "The vector size cannot be null");
            ArgumentOutOfRangeExceptionEx.ThrowIfZeroOrNegative(Size!.Value, nameof(Size), "The vector size must be greater than zero");
            ArgumentNullExceptionEx.ThrowIfNull(_distanceType, nameof(_distanceType), "The distance type has not been defined");
            ArgumentOutOfRangeExceptionEx.ThrowIfNot(
                _distanceType is QdrantDistanceType.Cosine or QdrantDistanceType.DotProduct or QdrantDistanceType.Euclidean or QdrantDistanceType.Manhattan,
                nameof(_distanceType),
                $"Distance type {_distanceType:G} not supported");
        }
    }


    private readonly string _collectionName;

    /// <summary>
    /// Collection settings consisting of a common vector length and vector distance calculation standard
    /// </summary>
    [JsonPropertyName("vectors")]
    public VectorSettings Settings { get; set; }


    public static CreateCollectionRequest Create(string collectionName, int vectorSize, QdrantDistanceType distanceType)
    {
        return new CreateCollectionRequest(collectionName, vectorSize, distanceType);
    }


    public HttpRequestMessage Build()
    {
        Settings.Validate();
        return HttpRequest.CreatePutRequest(
            $"collections/{_collectionName}?wait=true",
            this);
    }


    private CreateCollectionRequest(string collectionName, int vectorSize, QdrantDistanceType distanceType)
    {
        _collectionName = collectionName;
        Settings = new VectorSettings(vectorSize, distanceType);
    }
}
