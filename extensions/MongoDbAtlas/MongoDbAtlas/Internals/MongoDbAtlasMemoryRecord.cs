// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;

namespace Microsoft.KernelMemory.MongoDbAtlas.Internals;

internal sealed class MongoDbAtlasMemoryRecord
{
    public string Id { get; set; } = null!;

    public string Index { get; set; } = null!;

    public float[] Embedding { get; set; } = null!;

    public List<Tag> Tags { get; set; } = [];

    public List<Payload> Payloads { get; set; } = [];


    internal sealed class Payload
    {
        public Payload(string key, object value)
        {
            Key = key;
            Value = value;
        }


        public string Key { get; set; }
        public object Value { get; set; }
    }


    internal sealed class Tag
    {
        public Tag(string key, string?[] values)
        {
            Key = key;
            Values = values;
        }


        public string Key { get; set; }
        public string?[] Values { get; set; }
    }
}
