// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.AppBuilders;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.DocumentStorage;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory.Pipeline.Queue;
using Microsoft.KernelMemory.Search;

namespace Microsoft.KernelMemory;

/// <summary>
/// Kernel Memory builder.
/// </summary>
public sealed class KernelMemoryBuilder : IKernelMemoryBuilder
{
    private enum ClientTypes
    {
        Undefined,
        SyncServerless,
        AsyncService
    }


    // Proxy to the internal service collections, used to (optionally) inject dependencies
    // into the user application space
    private readonly ServiceCollectionPool _serviceCollections;

    // Services required to build the memory client class
    private readonly IServiceCollection _memoryServiceCollection;

    // Services of the host application
    private readonly IServiceCollection? _hostServiceCollection;

    // List of all the embedding generators to use during ingestion
    private readonly List<ITextEmbeddingGenerator> _embeddingGenerators = [];

    // List of all the memory DBs to use during ingestion
    private readonly List<IMemoryDb> _memoryDbs = [];

    // Normalized configuration
    private readonly KernelMemoryConfig? _memoryConfiguration = null;

    /// <summary>
    /// Whether to register the default handlers. The list is hardcoded.
    /// Additional handlers can be configured as "default", see appsettings.json
    /// but they must be registered manually, including their dependencies
    /// if they depend on third party components.
    /// </summary>
    private bool _useDefaultHandlers = true;

    /// <summary>
    /// Proxy to the internal service collections, used to (optionally) inject
    /// dependencies into the user application space
    /// </summary>
    public ServiceCollectionPool Services => _serviceCollections;


    /// <summary>
    /// Create a new instance of the builder
    /// </summary>
    /// <param name="hostServiceCollection">Host application service collection, required
    /// when hosting the pipeline handlers. The builder will register in this collection
    /// all the dependencies required by the handlers, such as storage, embedding generators,
    /// AI dependencies, orchestrator classes, etc.</param>
    public KernelMemoryBuilder(IServiceCollection? hostServiceCollection = null)
    {
        _memoryServiceCollection = new ServiceCollection();
        _hostServiceCollection = hostServiceCollection;

        // Support IHttpClientFactory (must be done before CopyServiceCollection)
        if (_hostServiceCollection == null) { _memoryServiceCollection.AddHttpClient(); }
        else { _hostServiceCollection.AddHttpClient(); }

        // Support request context
        if (_hostServiceCollection == null) { _memoryServiceCollection.AddRequestContextProvider(); }
        else
        {
            // Inject only if not already provided
            if (!_hostServiceCollection.HasService<IContextProvider>())
            {
                _hostServiceCollection.AddRequestContextProvider();
            }
        }

        CopyServiceCollection(hostServiceCollection, _memoryServiceCollection);

        // Important: this._memoryServiceCollection is the primary service collection
        _serviceCollections = new ServiceCollectionPool(_memoryServiceCollection);
        _serviceCollections.AddServiceCollection(_hostServiceCollection);

        // List of embedding generators and memory DBs used during the ingestion
        _embeddingGenerators.Clear();
        _memoryDbs.Clear();
        AddSingleton<List<ITextEmbeddingGenerator>>(_embeddingGenerators);
        AddSingleton<List<IMemoryDb>>(_memoryDbs);

        // Default configuration for tests and demos
        this.WithSimpleFileStorage(new SimpleFileStorageConfig { StorageType = FileSystemTypes.Volatile });
        this.WithSimpleVectorDb(new SimpleVectorDbConfig { StorageType = FileSystemTypes.Volatile });

        // Default dependencies, can be overridden
        this.WithDefaultMimeTypeDetection();
        this.WithDefaultPromptProvider();
        this.WithDefaultWebScraper();
        this.WithDefaultContentDecoders();
    }


    ///<inheritdoc />
    public IKernelMemory Build(KernelMemoryBuilderBuildOptions? options = null)
    {
        var type = GetBuildType();

        switch (type)
        {
            case ClientTypes.SyncServerless:
                return BuildServerlessClient(options);

            case ClientTypes.AsyncService:
                return BuildAsyncClient(options);

            case ClientTypes.Undefined:
                throw new ConfigurationException("Missing dependencies or insufficient configuration provided. " + "Try using With...() methods " + $"and other configuration methods before calling {nameof(this.Build)}(...)");

            default:
                throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported memory type '{type}'");
        }
    }


    ///<inheritdoc />
    public T Build<T>(KernelMemoryBuilderBuildOptions? options = null) where T : class, IKernelMemory
    {
        if (typeof(T) == typeof(MemoryServerless))
        {
            if (BuildServerlessClient(options) is not T result)
            {
                throw new InvalidOperationException($"Unable to instantiate '{typeof(MemoryServerless)}'. The instance is NULL.");
            }

            return result;
        }

        if (typeof(T) == typeof(MemoryService))
        {
            if (BuildAsyncClient(options) is not T result)
            {
                throw new InvalidOperationException($"Unable to instantiate '{typeof(MemoryService)}'. The instance is NULL.");
            }

            return result;
        }

        throw new KernelMemoryException($"The type of memory specified is not available, " + $"use either '{typeof(MemoryService)}' for the asynchronous memory with pipelines, " + $"or '{typeof(MemoryServerless)}' for the serverless synchronous memory client");
    }


    ///<inheritdoc />
    public IKernelMemoryBuilder AddSingleton<TService>(TService implementationInstance)
        where TService : class
    {
        Services.AddSingleton<TService>(implementationInstance);
        return this;
    }


    ///<inheritdoc />
    public IKernelMemoryBuilder AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        Services.AddSingleton<TService, TImplementation>();
        return this;
    }


    ///<inheritdoc />
    public IKernelMemoryBuilder WithoutDefaultHandlers()
    {
        _useDefaultHandlers = false;
        return this;
    }


    ///<inheritdoc />
    public IKernelMemoryBuilder AddIngestionMemoryDb(IMemoryDb service)
    {
        _memoryDbs.Add(service);
        return this;
    }


    ///<inheritdoc />
    public IKernelMemoryBuilder AddIngestionEmbeddingGenerator(ITextEmbeddingGenerator service)
    {
        _embeddingGenerators.Add(service);
        return this;
    }


    ///<inheritdoc />
    public IPipelineOrchestrator GetOrchestrator()
    {
        var serviceProvider = _memoryServiceCollection.BuildServiceProvider();
        return serviceProvider.GetService<IPipelineOrchestrator>() ?? throw new ConfigurationException("Memory Builder: unable to build orchestrator");
    }


    #region internals

    private static void CopyServiceCollection(
        IServiceCollection? source,
        IServiceCollection destination1,
        IServiceCollection? destination2 = null)
    {
        if (source == null) { return; }

        foreach (ServiceDescriptor d in source)
        {
            destination1.Add(d);
            destination2?.Add(d);
        }
    }


    private MemoryServerless BuildServerlessClient(KernelMemoryBuilderBuildOptions? options)
    {
        try
        {
            ServiceProvider serviceProvider = _memoryServiceCollection.BuildServiceProvider();
            CompleteServerlessClient(serviceProvider);

            // In case the user didn't set the embedding generator and memory DB to use for ingestion, use the values set for retrieval
            ReuseRetrievalEmbeddingGeneratorIfNecessary(serviceProvider);
            ReuseRetrievalMemoryDbIfNecessary(serviceProvider);
            CheckForMissingDependencies();

            // Recreate the service provider, in order to have the latest dependencies just configured
            serviceProvider = _memoryServiceCollection.BuildServiceProvider();
            CheckStoragePersistence(options, serviceProvider);
            var memoryClientInstance = ActivatorUtilities.CreateInstance<MemoryServerless>(serviceProvider);

            // Load handlers in the memory client
            if (_useDefaultHandlers)
            {
                memoryClientInstance.Orchestrator.AddDefaultHandlers();
            }

            return memoryClientInstance;
        }
        catch (Exception e)
        {
            ShowException(e);
            throw;
        }
    }


    private MemoryService BuildAsyncClient(KernelMemoryBuilderBuildOptions? options)
    {
        // Add handlers to DI service collection
        if (_useDefaultHandlers)
        {
            if (_hostServiceCollection == null)
            {
                throw new ConfigurationException("When using the Asynchronous Memory, Pipeline Handlers require a hosting application " + "(IHost, e.g. Host or WebApplication) to run as services (IHostedService). " + "Please instantiate KernelMemoryBuilder passing the host application ServiceCollection.");
            }

            this.WithDefaultHandlersAsHostedServices(_hostServiceCollection);
        }

        ServiceProvider serviceProvider = _memoryServiceCollection.BuildServiceProvider();
        CompleteAsyncClient(serviceProvider);

        // In case the user didn't set the embedding generator and memory DB to use for ingestion, use the values set for retrieval
        ReuseRetrievalEmbeddingGeneratorIfNecessary(serviceProvider);
        ReuseRetrievalMemoryDbIfNecessary(serviceProvider);
        CheckForMissingDependencies();

        // Recreate the service provider, in order to have the latest dependencies just configured
        serviceProvider = _memoryServiceCollection.BuildServiceProvider();
        CheckStoragePersistence(options, serviceProvider);
        return ActivatorUtilities.CreateInstance<MemoryService>(serviceProvider);
    }


    private void CheckStoragePersistence(KernelMemoryBuilderBuildOptions? options, ServiceProvider serviceProvider)
    {
        if (options is { AllowMixingVolatileAndPersistentData: true }) { return; }

        ServiceDescriptor docStoreType = _memoryServiceCollection.Last<ServiceDescriptor>(x => x.ServiceType == typeof(IDocumentStorage));
        ServiceDescriptor memStoreType = _memoryServiceCollection.Last<ServiceDescriptor>(x => x.ServiceType == typeof(IMemoryDb));
        SimpleFileStorageConfig? simpleFileStorageConfig = serviceProvider.GetService<SimpleFileStorageConfig>();
        SimpleVectorDbConfig? simpleVectorDbConfig = serviceProvider.GetService<SimpleVectorDbConfig>();
        SimpleTextDbConfig? simpleTextDbConfig = serviceProvider.GetService<SimpleTextDbConfig>();

        bool persistentDocStore = docStoreType.ImplementationType != typeof(SimpleFileStorage) || simpleFileStorageConfig?.StorageType == FileSystemTypes.Disk;
        bool persistentMemStore = memStoreType.ImplementationType != typeof(SimpleVectorDb) && memStoreType.ImplementationType != typeof(SimpleTextDb)
            || memStoreType.ImplementationType == typeof(SimpleVectorDb) && simpleVectorDbConfig?.StorageType == FileSystemTypes.Disk
            || memStoreType.ImplementationType == typeof(SimpleTextDb) && simpleTextDbConfig?.StorageType == FileSystemTypes.Disk;

        // No error if both services are volatile or persistent,
        if (persistentMemStore == persistentDocStore)
        {
            return;
        }

        // Show a service name for a helpful error message
        var docStoreName = docStoreType.ImplementationType != typeof(SimpleFileStorage)
            ? docStoreType.ImplementationType?.Name
            : $"{nameof(SimpleFileStorage)} {simpleFileStorageConfig?.StorageType}";

        var memStoreName = memStoreType.ImplementationType != typeof(SimpleVectorDb) && memStoreType.ImplementationType != typeof(SimpleTextDb)
            ? memStoreType.ImplementationType?.Name
            : memStoreType.ImplementationType == typeof(SimpleVectorDb)
                ? $"{nameof(SimpleVectorDb)} {simpleVectorDbConfig?.StorageType}"
                : $"{nameof(SimpleTextDb)} {simpleTextDbConfig?.StorageType}";

        // Different error message depending on which service is volatile
        if (persistentMemStore && !persistentDocStore)
        {
            throw new ConfigurationException(
                $"Using a persistent memory store ({memStoreName}) with a volatile document store ({docStoreName}) will lead to duplicate memory records over multiple executions. "
                + $"Set up Kernel Memory to use a persistent document store like Azure Blobs, AWS S3, {nameof(SimpleFileStorage)} on disk, etc. "
                + $"Otherwise, use {nameof(KernelMemoryBuilderBuildOptions)}.{nameof(KernelMemoryBuilderBuildOptions.AllowMixingVolatileAndPersistentData)} "
                + "to suppress this exception when invoking kernelMemoryBuilder.Build(<options here>). ");
        }

        if (persistentDocStore && !persistentMemStore)
        {
            throw new ConfigurationException(
                $"Using a volatile memory store ({memStoreName}) with a persistent document store ({docStoreName}) will lead to missing memory records over multiple executions. "
                + $"Set up Kernel Memory to use a persistent memory store like Azure AI Search, Postgres, Qdrant, {nameof(SimpleVectorDb)} on disk, etc. "
                + $"Otherwise, use {nameof(KernelMemoryBuilderBuildOptions)}.{nameof(KernelMemoryBuilderBuildOptions.AllowMixingVolatileAndPersistentData)} "
                + "to suppress this exception when invoking kernelMemoryBuilder.Build(<options here>). ");
        }
    }


    private KernelMemoryBuilder CompleteServerlessClient(ServiceProvider serviceProvider)
    {
        UseDefaultSearchClientIfNecessary(serviceProvider);
        AddSingleton<IPipelineOrchestrator, InProcessPipelineOrchestrator>();
        AddSingleton<InProcessPipelineOrchestrator, InProcessPipelineOrchestrator>();
        return this;
    }


    private KernelMemoryBuilder CompleteAsyncClient(ServiceProvider serviceProvider)
    {
        UseDefaultSearchClientIfNecessary(serviceProvider);
        AddSingleton<IPipelineOrchestrator, DistributedPipelineOrchestrator>();
        AddSingleton<DistributedPipelineOrchestrator, DistributedPipelineOrchestrator>();
        return this;
    }


    private void CheckForMissingDependencies()
    {
        RequireEmbeddingGenerator();
        RequireOneMemoryDbForIngestion();
        RequireOneMemoryDbForRetrieval();
    }


    private void RequireEmbeddingGenerator()
    {
        if (IsEmbeddingGeneratorEnabled() && _embeddingGenerators.Count == 0)
        {
            throw new ConfigurationException("Memory Builder: no embedding generators configured for memory ingestion. Check 'EmbeddingGeneratorTypes' setting.");
        }
    }


    private void RequireOneMemoryDbForIngestion()
    {
        if (_memoryDbs.Count == 0)
        {
            throw new ConfigurationException("Memory Builder: memory DBs for ingestion not configured");
        }
    }


    private void RequireOneMemoryDbForRetrieval()
    {
        if (!_memoryServiceCollection.HasService<IMemoryDb>())
        {
            throw new ConfigurationException("Memory Builder: memory DBs for retrieval not configured");
        }
    }


    private void UseDefaultSearchClientIfNecessary(ServiceProvider serviceProvider)
    {
        if (!_memoryServiceCollection.HasService<ISearchClient>())
        {
            this.WithDefaultSearchClient(serviceProvider.GetService<SearchClientConfig>());
        }
    }


    private void ReuseRetrievalEmbeddingGeneratorIfNecessary(IServiceProvider serviceProvider)
    {
        if (_embeddingGenerators.Count == 0 && _memoryServiceCollection.HasService<ITextEmbeddingGenerator>())
        {
            _embeddingGenerators.Add(serviceProvider.GetService<ITextEmbeddingGenerator>()
                ?? throw new ConfigurationException("Memory Builder: unable to build embedding generator"));
        }
    }


    private void ReuseRetrievalMemoryDbIfNecessary(IServiceProvider serviceProvider)
    {
        if (_memoryDbs.Count == 0 && _memoryServiceCollection.HasService<IMemoryDb>())
        {
            _memoryDbs.Add(serviceProvider.GetService<IMemoryDb>()
                ?? throw new ConfigurationException("Memory Builder: unable to build memory DB instance"));
        }
    }


    private bool IsEmbeddingGeneratorEnabled()
    {
        return _memoryConfiguration is null or { DataIngestion.EmbeddingGenerationEnabled: true };
    }


    private ClientTypes GetBuildType()
    {
        var hasQueueFactory = _memoryServiceCollection.HasService<QueueClientFactory>();
        var hasDocumentStorage = _memoryServiceCollection.HasService<IDocumentStorage>();
        var hasMimeDetector = _memoryServiceCollection.HasService<IMimeTypeDetection>();
        var hasEmbeddingGenerator = _memoryServiceCollection.HasService<ITextEmbeddingGenerator>();
        var hasMemoryDb = _memoryServiceCollection.HasService<IMemoryDb>();
        var hasTextGenerator = _memoryServiceCollection.HasService<ITextGenerator>();

        if (hasDocumentStorage && hasMimeDetector && hasEmbeddingGenerator && hasMemoryDb && hasTextGenerator)
        {
            return hasQueueFactory
                ? ClientTypes.AsyncService
                : ClientTypes.SyncServerless;
        }

        var missing = new List<string>();

        if (!hasDocumentStorage) { missing.Add("Document storage"); }

        if (!hasMimeDetector) { missing.Add("MIME type detection"); }

        if (!hasEmbeddingGenerator) { missing.Add("Embedding generator"); }

        if (!hasMemoryDb) { missing.Add("Memory DB"); }

        if (!hasTextGenerator) { missing.Add("Text generator"); }

        throw new ConfigurationException("Memory Builder: cannot build Memory client, some dependencies are not defined: " + string.Join(", ", missing));
    }


    /// <summary>
    /// Basic helper for debugging issues in the memory builder
    /// </summary>
    private static void ShowException(Exception e)
    {
        if (e.StackTrace == null) { return; }

        string location = e.StackTrace.Trim()
            .Replace(" in ", "\n            in: ", StringComparison.OrdinalIgnoreCase)
            .Replace(":line ", "\n            line: ", StringComparison.OrdinalIgnoreCase);
        int pos = location.IndexOf("dotnet/", StringComparison.OrdinalIgnoreCase);

        if (pos > 0) { location = location.Substring(pos); }

        Console.Write($"## Error ##\n* Message:  {e.Message}\n* Type:     {e.GetType().Name} [{e.GetType().FullName}]\n* Location: {location}\n## ");
    }

    #endregion


}
