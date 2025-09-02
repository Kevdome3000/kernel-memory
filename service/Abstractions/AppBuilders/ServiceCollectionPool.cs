// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.KernelMemory.AppBuilders;

/// <summary>
/// Represents a collection of service collections, so that DI helpers
/// like `WithX` act on multiple service collections, e.g. the one used
/// by KernelMemoryBuilder and the one used by end user application.
///
/// The pool is meant to have a "primary" that contains all services,
/// so that it's possible to look up the aggregate, e.g. check if
/// a dependency exists in any of the collections, and to loop
/// through the complete list of service descriptors.
/// </summary>
[Experimental("KMEXP00")]
public sealed class ServiceCollectionPool : IServiceCollection
{
    /// <summary>
    /// Collection of service collections, ie the pool.
    /// </summary>
    private readonly List<IServiceCollection> _pool;

    /// <summary>
    /// Primary collection used for read and iteration calls
    /// </summary>
    private readonly IServiceCollection _primaryCollection;

    /// <summary>
    /// Flag indicating whether the list of collections is readonly.
    /// The list becomes readonly as soon as service descriptors are added.
    /// </summary>
    private bool _poolSizeLocked;

    /// <summary>
    /// The total number of service descriptors registered
    /// </summary>
    public int Count => _primaryCollection.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => _primaryCollection.IsReadOnly;


    /// <summary>
    /// Create a new instance, passing in the primary list of services
    /// </summary>
    /// <param name="primaryCollection">The primary service collection</param>
    public ServiceCollectionPool(IServiceCollection primaryCollection)
    {
        ArgumentNullExceptionEx.ThrowIfNull(primaryCollection, nameof(primaryCollection), "The primary service collection cannot be NULL");
        _poolSizeLocked = false;
        _primaryCollection = primaryCollection;
        _pool = [primaryCollection];
    }


    /// <summary>
    /// Add one more service collection to the pool
    /// </summary>
    /// <param name="serviceCollection">Service collection</param>
    public void AddServiceCollection(IServiceCollection? serviceCollection)
    {
        if (serviceCollection == null) { return; }

        if (_poolSizeLocked)
        {
            throw new InvalidOperationException("The pool of service collections is already in use and cannot be extended");
        }

        _pool.Add(serviceCollection);
    }


    /// <inheritdoc/>
    public void Add(ServiceDescriptor item)
    {
        Lock();

        foreach (var sc in _pool)
        {
            sc.Add(item);
        }
    }


    /// <inheritdoc/>
    public bool Contains(ServiceDescriptor item)
    {
        Lock();
        return _pool.First().Contains(item);
    }


    /* IMPORTANT: iterations use the primary collection only. */


    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        Lock();
        return _primaryCollection.GetEnumerator();
    }


    /// <inheritdoc/>
    public IEnumerator<ServiceDescriptor> GetEnumerator()
    {
        Lock();
        return _primaryCollection.GetEnumerator();
    }


    /// <inheritdoc/>
    public bool Remove(ServiceDescriptor item)
    {
        Lock();

        var result = false;

        foreach (var service in _pool)
        {
            result = result || service.Remove(item);
        }

        return result;
    }


    /// <inheritdoc/>
    public void Clear()
    {
        Lock();

        foreach (var service in _pool)
        {
            service.Clear();
        }
    }


    #region unsafe

    /// <inheritdoc/>
    public void CopyTo(ServiceDescriptor[] array, int arrayIndex)
    {
        Lock();

        // When using multiple service providers, the position is not consistent
        // If you need this API, e.g. to loop through the service descriptors:
        // * loop using the enumerator
        if (_pool.Count != 1) { throw AccessByPositionNotAllowed(); }

        _primaryCollection.CopyTo(array, arrayIndex);
    }


    /// <inheritdoc/>
    public void Insert(int index, ServiceDescriptor item)
    {
        Lock();

        // When using multiple service providers, the position is not consistent
        // If you need this API, e.g. to loop through the service descriptors:
        // * create a custom service collection and pass it to KernelMemoryBuilder ctor
        if (_pool.Count != 1) { throw AccessByPositionNotAllowed(); }

        _primaryCollection.Insert(index, item);
    }


    /// <inheritdoc/>
    public int IndexOf(ServiceDescriptor item)
    {
        Lock();

        // When using multiple service providers, the position is not consistent
        // If you need this API, e.g. to loop through the service descriptors:
        // * loop using the enumerator
        // * create a custom service collection and pass it to KernelMemoryBuilder ctor
        if (_pool.Count != 1) { throw AccessByPositionNotAllowed(); }

        return _primaryCollection.IndexOf(item);
    }


    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        Lock();

        // When using multiple service providers, the position is not consistent
        // If you need this API, e.g. to loop through the service descriptors:
        // * loop using the enumerator
        // * create a custom service collection and pass it to KernelMemoryBuilder ctor
        if (_pool.Count != 1) { throw AccessByPositionNotAllowed(); }

        _primaryCollection.RemoveAt(index);
    }


    /// <inheritdoc/>
    public ServiceDescriptor this[int index]
    {
        get
        {
            Lock();

            // When using multiple service providers, the position is not consistent
            // If you need this API, e.g. to loop through the service descriptors:
            // * loop using the enumerator
            // * create a custom service collection and pass it to KernelMemoryBuilder ctor
            if (_pool.Count != 1) { throw AccessByPositionNotAllowed(); }

            return _primaryCollection[index];
        }
        set
        {
            Lock();

            // When using multiple service providers, the position is not consistent
            // If you need this API, e.g. to loop through the service descriptors:
            // * create a custom service collection and pass it to KernelMemoryBuilder ctor
            if (_pool.Count != 1) { throw AccessByPositionNotAllowed(); }

            _primaryCollection[index] = value;
        }
    }

    #endregion


    private void Lock()
    {
        _poolSizeLocked = true;
    }


    /// <exception cref="InvalidOperationException"></exception>
    private static InvalidOperationException AccessByPositionNotAllowed()
    {
        return new InvalidOperationException(
            $"{nameof(ServiceCollectionPool)} contains collections of different size, " + "and direct access by position is not allowed, to avoid inconsistent results.");
    }


#pragma warning restore CA1065
}
