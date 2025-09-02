// Copyright (c) Microsoft.All rights reserved.

using System;

namespace Microsoft.KernelMemory.Chunkers;

public class MarkDownChunkerOptions
{
    private int _maxTokensPerChunk = 1024;
    private int _overlap = 0;

    /// <summary>
    /// Maximum number of tokens per chunk (must be > 0)
    /// </summary>
    public int MaxTokensPerChunk
    {
        get => _maxTokensPerChunk;
        set => _maxTokensPerChunk = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxTokensPerChunk), "Value must be greater than zero.");
    }

    /// <summary>
    /// Number of tokens to copy and repeat from a chunk into the next (must be >= 0)
    /// </summary>
    public int Overlap
    {
        get => _overlap;
        set => _overlap = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Overlap), "Value must be zero or greater.");
    }

    /// <summary>
    /// Optional header to add before each chunk.
    /// </summary>
    public string? ChunkHeader { get; set; } = null;
}
