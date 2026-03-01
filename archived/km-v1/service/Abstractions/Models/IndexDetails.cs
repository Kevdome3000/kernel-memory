// Copyright (c) Microsoft.All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.KernelMemory.Models;

public class IndexDetails
{
    [JsonPropertyName("name")]
    [JsonPropertyOrder(1)]
    public string Name { get; set; } = string.Empty;
}
