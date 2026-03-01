// Copyright (c) Microsoft. All rights reserved.
using KernelMemory.Core.Config.Enums;
using KernelMemory.Core.Embeddings.Cache;
using Microsoft.Extensions.Logging;

namespace KernelMemory.Core.Embeddings;

/// <summary>
/// Decorator that wraps an IEmbeddingGenerator with caching support.
/// Checks the cache before generating embeddings, and stores new embeddings in the cache.
/// Supports different cache modes (ReadWrite, ReadOnly, WriteOnly).
/// </summary>
public sealed class CachedEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly IEmbeddingGenerator _inner;
    private readonly IEmbeddingCache _cache;
    private readonly ILogger<CachedEmbeddingGenerator> _logger;

    /// <inheritdoc />
    public EmbeddingsTypes ProviderType => _inner.ProviderType;

    /// <inheritdoc />
    public string ModelName => _inner.ModelName;

    /// <inheritdoc />
    public int VectorDimensions => _inner.VectorDimensions;

    /// <inheritdoc />
    public bool IsNormalized => _inner.IsNormalized;


    /// <summary>
    /// Creates a new cached embedding generator decorator.
    /// </summary>
    /// <param name="inner">The inner generator to wrap.</param>
    /// <param name="cache">The cache to use for storing/retrieving embeddings.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">When inner, cache, or logger is null.</exception>
    public CachedEmbeddingGenerator(
        IEmbeddingGenerator inner,
        IEmbeddingCache cache,
        ILogger<CachedEmbeddingGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _cache = cache;
        _logger = logger;

        _logger.LogDebug(
            "CachedEmbeddingGenerator initialized for {Provider}/{Model} with cache mode {Mode}",
            inner.ProviderType,
            inner.ModelName,
            cache.Mode);
    }


    /// <inheritdoc />
    public async Task<EmbeddingResult> GenerateAsync(string text, CancellationToken ct = default)
    {
        var key = BuildCacheKey(text);

        // Try cache read (if mode allows)
        if (_cache.Mode != CacheModes.WriteOnly)
        {
            var cached = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);

            if (cached != null)
            {
                _logger.LogDebug("Cache hit for single embedding, dimensions: {Dimensions}", cached.Vector.Length);
                // Return cached result with token count if available
                return cached.TokenCount.HasValue
                    ? EmbeddingResult.FromVectorWithTokens(cached.Vector, cached.TokenCount.Value)
                    : EmbeddingResult.FromVector(cached.Vector);
            }
        }

        // Generate embedding
        _logger.LogDebug("Cache miss for single embedding, calling {Provider}", ProviderType);
        var result = await _inner.GenerateAsync(text, ct).ConfigureAwait(false);

        // Store in cache (if mode allows)
        if (_cache.Mode != CacheModes.ReadOnly)
        {
            await _cache.StoreAsync(key,
                    result.Vector,
                    result.TokenCount,
                    ct)
                .ConfigureAwait(false);
            _logger.LogDebug("Stored embedding in cache, dimensions: {Dimensions}, tokenCount: {TokenCount}",
                result.Vector.Length,
                result.TokenCount);
        }

        return result;
    }


    /// <inheritdoc />
    public async Task<EmbeddingResult[]> GenerateAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var textList = texts.ToList();

        if (textList.Count == 0)
        {
            return [];
        }

        // Initialize result array with nulls
        var results = new EmbeddingResult?[textList.Count];

        // Track which texts need to be generated
        var toGenerate = new List<(int Index, string Text)>();

        // Try cache reads (if mode allows)
        if (_cache.Mode != CacheModes.WriteOnly)
        {
            for (int i = 0; i < textList.Count; i++)
            {
                var key = BuildCacheKey(textList[i]);
                var cached = await _cache.TryGetAsync(key, ct).ConfigureAwait(false);

                if (cached != null)
                {
                    // Return cached result with token count if available
                    results[i] = cached.TokenCount.HasValue
                        ? EmbeddingResult.FromVectorWithTokens(cached.Vector, cached.TokenCount.Value)
                        : EmbeddingResult.FromVector(cached.Vector);
                }
                else
                {
                    toGenerate.Add((i, textList[i]));
                }
            }

            _logger.LogDebug(
                "Batch cache lookup: {HitCount} hits, {MissCount} misses",
                textList.Count - toGenerate.Count,
                toGenerate.Count);
        }
        else
        {
            // WriteOnly mode - all texts need to be generated
            for (int i = 0; i < textList.Count; i++)
            {
                toGenerate.Add((i, textList[i]));
            }
        }

        // Generate missing embeddings
        if (toGenerate.Count > 0)
        {
            var textsToGenerate = toGenerate.Select(x => x.Text);
            var generatedResults = await _inner.GenerateAsync(textsToGenerate, ct).ConfigureAwait(false);

            // Map generated results back to results array and store in cache
            for (int i = 0; i < toGenerate.Count; i++)
            {
                var (originalIndex, text) = toGenerate[i];
                results[originalIndex] = generatedResults[i];

                // Store in cache (if mode allows)
                if (_cache.Mode != CacheModes.ReadOnly)
                {
                    var key = BuildCacheKey(text);
                    await _cache.StoreAsync(key,
                            generatedResults[i].Vector,
                            generatedResults[i].TokenCount,
                            ct)
                        .ConfigureAwait(false);
                }
            }

            _logger.LogDebug("Generated and cached {Count} embeddings", toGenerate.Count);
        }

        // Convert nullable array to non-nullable (all slots should be filled now)
        return results.Select(r => r!).ToArray();
    }


    /// <summary>
    /// Builds a cache key for the given text using the inner generator's properties.
    /// </summary>
    private EmbeddingCacheKey BuildCacheKey(string text)
    {
        return EmbeddingCacheKey.Create(
            _inner.ProviderType.ToString(),
            _inner.ModelName,
            _inner.VectorDimensions,
            _inner.IsNormalized,
            text);
    }
}
