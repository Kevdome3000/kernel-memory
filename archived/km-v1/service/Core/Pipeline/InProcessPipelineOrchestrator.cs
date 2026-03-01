// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.DocumentStorage;
using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.MemoryStorage;

namespace Microsoft.KernelMemory.Pipeline;

[Experimental("KMEXP04")]
public sealed class InProcessPipelineOrchestrator : BaseOrchestrator
{
    private readonly Dictionary<string, IPipelineStepHandler> _handlers = new(StringComparer.InvariantCultureIgnoreCase);

    private readonly IServiceProvider? _serviceProvider;


    /// <summary>
    /// Create a new instance of the synchronous orchestrator.
    /// </summary>
    /// <param name="documentStorage">Service used to store files</param>
    /// <param name="embeddingGenerators">Services used to generate embeddings during the ingestion</param>
    /// <param name="memoryDbs">Services where to store memory records</param>
    /// <param name="textGenerator">Service used to generate text, e.g. synthetic memory records</param>
    /// <param name="mimeTypeDetection">Service used to detect a file type</param>
    /// <param name="serviceProvider">Optional service provider to add handlers by type</param>
    /// <param name="config">Global KM configuration</param>
    /// <param name="loggerFactory">Application logger factory</param>
    public InProcessPipelineOrchestrator(
        IDocumentStorage documentStorage,
        List<ITextEmbeddingGenerator> embeddingGenerators,
        List<IMemoryDb> memoryDbs,
        ITextGenerator textGenerator,
        IMimeTypeDetection? mimeTypeDetection = null,
        IServiceProvider? serviceProvider = null,
        KernelMemoryConfig? config = null,
        ILoggerFactory? loggerFactory = null)
        : base(documentStorage,
            embeddingGenerators,
            memoryDbs,
            textGenerator,
            mimeTypeDetection,
            config,
            loggerFactory?.CreateLogger<InProcessPipelineOrchestrator>())
    {
        _serviceProvider = serviceProvider;
    }


    ///<inheritdoc />
    public override List<string> HandlerNames => _handlers.Keys.OrderBy(x => x).ToList();


    ///<inheritdoc />
    public override Task AddHandlerAsync(
        IPipelineStepHandler handler,
        CancellationToken cancellationToken = default)
    {
        AddHandler(handler);
        return Task.CompletedTask;
    }


    ///<inheritdoc />
    public override Task TryAddHandlerAsync(IPipelineStepHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(handler.StepName, nameof(handler.StepName), "The step name is empty");

        if (_handlers.ContainsKey(handler.StepName)) { return Task.CompletedTask; }

        try
        {
#pragma warning disable CA1849 // AddHandler doesn't do any I/O
            AddHandler(handler);
#pragma warning restore CA1849
        }
        catch (ArgumentException)
        {
            // TODO: use a more specific exception
            // Ignore
        }

        return Task.CompletedTask;
    }


    /// <summary>
    /// Register a pipeline handler. If a handler for the same step name already exists, it gets replaced.
    /// </summary>
    /// <param name="stepName">Name of the queue/step associated with the handler</param>
    /// <typeparam name="T">Handler class</typeparam>
    public void AddHandler<T>(string stepName) where T : IPipelineStepHandler
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider is undefined. Try using <.AddHandler(handler instance)> method instead.");
        }

        AddHandler(ActivatorUtilities.CreateInstance<T>(_serviceProvider, stepName));
    }


    /// <summary>
    /// Register a pipeline handler.
    /// </summary>
    /// <param name="config">Handler type configuration</param>
    /// <param name="stepName">Pipeline step name</param>
    public void AddSynchronousHandler(HandlerConfig config, string stepName)
    {
        if (HandlerTypeLoader.TryGetHandlerType(config, out var handlerType))
        {
            AddHandler(handlerType, stepName);
        }
    }


    /// <summary>
    /// Register a pipeline handler.
    /// </summary>
    /// <param name="handlerType">Handler class</param>
    /// <param name="stepName">Name of the queue/step associated with the handler</param>
    public void AddHandler(Type handlerType, string stepName)
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider is undefined. Try using <.AddHandler(handler instance)> method instead.");
        }

        var handler = ActivatorUtilities.CreateInstance(_serviceProvider, handlerType, stepName);

        if (handler is not IPipelineStepHandler)
        {
            throw new InvalidOperationException($"Type '{handlerType}' is not valid: {nameof(IPipelineStepHandler)} not implemented.");
        }

        AddHandler((IPipelineStepHandler)handler);
    }


    /// <summary>
    /// Synchronous (queue less) version of AddHandlerAsync. Register a pipeline handler.
    /// If a handler for the same step name already exists, it gets replaced.
    /// </summary>
    /// <param name="handler">Pipeline handler instance</param>
    public void AddHandler(IPipelineStepHandler handler)
    {
        ArgumentNullExceptionEx.ThrowIfNull(handler, nameof(handler), "The handler is NULL");
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(handler.StepName, nameof(handler.StepName), "The step name is empty");

        if (!_handlers.TryAdd(handler.StepName, handler))
        {
            throw new ArgumentException($"There is already a handler for step '{handler.StepName}'");
        }
    }


    ///<inheritdoc />
    public override async Task RunPipelineAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
        // Files must be uploaded before starting any other task
        await UploadFilesAsync(pipeline, cancellationToken).ConfigureAwait(false);

        await UpdatePipelineStatusAsync(pipeline, cancellationToken).ConfigureAwait(false);

        while (!pipeline.Complete)
        {
            string currentStepName = pipeline.RemainingSteps.First();

            if (!_handlers.TryGetValue(currentStepName, out var stepHandler))
            {
                throw new OrchestrationException($"No handlers found for step '{currentStepName}'");
            }

            // Run handler
            (ReturnType returnType, DataPipeline updatedPipeline) = await stepHandler
                .InvokeAsync(pipeline, CancellationTokenSource.Token)
                .ConfigureAwait(false);

            switch (returnType)
            {
                case ReturnType.Success:
                    pipeline = updatedPipeline;
                    pipeline.LastUpdate = DateTimeOffset.UtcNow;
                    Log.LogInformation("Handler '{0}' processed pipeline '{1}/{2}' successfully",
                        currentStepName,
                        pipeline.Index,
                        pipeline.DocumentId);
                    pipeline.MoveToNextStep();
                    await UpdatePipelineStatusAsync(pipeline, cancellationToken).ConfigureAwait(false);
                    break;

                case ReturnType.TransientError:
                    Log.LogError("Handler '{0}' failed to process pipeline '{1}/{2}'",
                        currentStepName,
                        pipeline.Index,
                        pipeline.DocumentId);
                    throw new OrchestrationException($"Pipeline error, step {currentStepName} failed", true);

                case ReturnType.FatalError:
                    Log.LogError("Handler '{0}' failed to process pipeline '{1}/{2}' due to an unrecoverable error",
                        currentStepName,
                        pipeline.Index,
                        pipeline.DocumentId);
                    throw new OrchestrationException($"Unrecoverable pipeline error, step {currentStepName} failed and cannot be retried", false);

                default:
                    throw new ArgumentOutOfRangeException($"Unknown {returnType:G} return type");
            }
        }

        await CleanUpAfterCompletionAsync(pipeline, cancellationToken).ConfigureAwait(false);

        Log.LogInformation("Pipeline '{0}/{1}' complete", pipeline.Index, pipeline.DocumentId);
    }
}
