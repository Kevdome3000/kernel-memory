// Copyright (c) Microsoft.All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.Models;

public class IndexCollection
{
    [JsonPropertyName("results")]
    [JsonPropertyOrder(1)]
    public List<IndexDetails> Results { get; set; } = [];
}
