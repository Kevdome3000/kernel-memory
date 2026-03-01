// Copyright (c) Microsoft. All rights reserved.
using KernelMemory.Core.Search;
using KernelMemory.Core.Storage;
using KernelMemory.Core.Storage.Models;

namespace KernelMemory.Main.Services;

/// <summary>
/// Business logic layer for content operations.
/// Wraps IContentStorage and provides CLI-friendly interface.
/// Implements IDisposable to ensure search indexes are properly disposed.
/// </summary>
public sealed class ContentService : IDisposable
{
    private readonly IContentStorage _storage;
    private readonly string _nodeId;
    private readonly IReadOnlyDictionary<string, ISearchIndex>? _searchIndexes;
    private bool _disposed;


    /// <summary>
    /// Initializes a new instance of ContentService.
    /// </summary>
    /// <param name="storage">The content storage implementation.</param>
    /// <param name="nodeId">The node ID this service operates on.</param>
    /// <param name="searchIndexes">Optional search indexes to dispose when done.</param>
    public ContentService(IContentStorage storage, string nodeId, IReadOnlyDictionary<string, ISearchIndex>? searchIndexes = null)
    {
        _storage = storage;
        _nodeId = nodeId;
        _searchIndexes = searchIndexes;
    }


    /// <summary>
    /// Gets the node ID this service operates on.
    /// </summary>
    public string NodeId => _nodeId;

    /// <summary>
    /// Gets the underlying content storage implementation.
    /// </summary>
    public IContentStorage Storage => _storage;

    /// <summary>
    /// Gets the registered search indexes for this service.
    /// </summary>
    public IReadOnlyDictionary<string, ISearchIndex> SearchIndexes => _searchIndexes ?? new Dictionary<string, ISearchIndex>();


    /// <summary>
    /// Upserts content and returns the write result.
    /// </summary>
    /// <param name="request">The upsert request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>WriteResult with ID and completion status.</returns>
    public async Task<WriteResult> UpsertAsync(UpsertRequest request, CancellationToken cancellationToken = default)
    {
        return await _storage.UpsertAsync(request, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Gets content by ID.
    /// </summary>
    /// <param name="id">The content ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content DTO, or null if not found.</returns>
    public async Task<ContentDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _storage.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Deletes content by ID.
    /// </summary>
    /// <param name="id">The content ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>WriteResult with ID and completion status.</returns>
    public async Task<WriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _storage.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Lists content with pagination.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of content DTOs.</returns>
    public async Task<List<ContentDto>> ListAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        return await _storage.ListAsync(skip, take, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Gets total count of content items.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total count.</returns>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _storage.CountAsync(cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Disposes the service and underlying search indexes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Dispose all search indexes (e.g., SqliteFtsIndex connections)
        if (_searchIndexes != null)
        {
            foreach (var index in _searchIndexes.Values)
            {
                if (index is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
