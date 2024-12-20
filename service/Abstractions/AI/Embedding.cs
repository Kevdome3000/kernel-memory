﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable IDE0130 // first class concept we want to have readily available
#pragma warning disable CA2225 // no need for explicit methods

// ReSharper disable once CheckNamespace - reduce number of "using" statements
namespace Microsoft.KernelMemory;

/// <summary>
/// Note: use Embedding.JsonConverter to serialize objects using this type.
/// Example:
///     [JsonPropertyName("vector")]
///     [JsonConverter(typeof(Embedding.JsonConverter))]
///     public Embedding Vector { get; set; }
/// </summary>
public struct Embedding : IEquatable<Embedding>
{
    /// <summary>
    /// Note: use Embedding.JsonConverter to serialize objects using this type.
    /// </summary>
    [JsonIgnore]
    public ReadOnlyMemory<float> Data { get; set; } = new();

    /// <summary>
    /// Note: use Embedding.JsonConverter to serialize objects using this type.
    /// </summary>
    [JsonIgnore]
    public readonly int Length => this.Data.Length;

    public Embedding(float[] vector)
    {
        this.Data = vector;
    }

    /// <summary>
    /// This is not a ctor on purpose so we can use collections syntax with
    /// the main ctor, and surface the extra casting cost when not using floats.
    /// </summary>
    public static Embedding FromDoubles(double[] vector)
    {
        float[] f = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++) { f[i] = (float)vector[i]; }

        return new Embedding(f);
    }

    public Embedding(ReadOnlyMemory<float> vector)
    {
        this.Data = vector;
    }

    public Embedding(int size)
    {
        this.Data = new ReadOnlyMemory<float>(new float[size]);
    }

    public readonly double CosineSimilarity(Embedding embedding)
    {
        var size1 = this.Data.Span.Length;
        var size2 = embedding.Data.Span.Length;
        if (size1 != size2)
        {
            throw new InvalidOperationException(
                "Embedding vectors must have the same length to calculate cosine similarity. " +
                $"Embedding 1 length: {size1}; Embedding 2 length: {size2}.");
        }

        return TensorPrimitives.CosineSimilarity(this.Data.Span, embedding.Data.Span);
    }

    /// <summary>
    /// Convert Semantic Kernel data type
    /// </summary>
    public static implicit operator Embedding(ReadOnlyMemory<float> data) => new(data);

    /// <summary>
    /// Allows simple embedding definition using float[]
    /// </summary>
    public static implicit operator Embedding(float[] data) => new(data);

    public readonly bool Equals(Embedding other) => this.Data.Equals(other.Data);

    public override readonly bool Equals(object? obj) => (obj is Embedding other && this.Equals(other));

    public static bool operator ==(Embedding v1, Embedding v2) => v1.Equals(v2);

    public static bool operator !=(Embedding v1, Embedding v2) => !(v1 == v2);

    public override readonly int GetHashCode() => this.Data.GetHashCode();

    /// <summary>
    /// Note: use Embedding.JsonConverter to serialize objects using
    /// the Embedding type, for example:
    ///     [JsonPropertyName("vector")]
    ///     [JsonConverter(typeof(Embedding.JsonConverter))]
    ///     public Embedding Vector { get; set; }
    /// </summary>
    public sealed class JsonConverter : JsonConverter<Embedding>
    {
        /// <summary>An instance of a converter for float[] that all operations delegate to</summary>
        private static readonly JsonConverter<float[]> s_converter =
            (JsonConverter<float[]>)new JsonSerializerOptions().GetConverter(typeof(float[]));

        public override Embedding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new Embedding(s_converter.Read(ref reader, typeof(float[]), options) ?? []);
        }

        public override void Write(Utf8JsonWriter writer, Embedding value, JsonSerializerOptions options)
        {
            s_converter.Write(writer, MemoryMarshal.TryGetArray(value.Data, out ArraySegment<float> array) && array.Count == value.Length
                ? array.Array!
                : value.Data.ToArray(), options);
        }
    }
}
