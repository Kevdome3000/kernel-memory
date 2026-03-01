// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;

namespace _202_dotnet_custom_handler_as_a_service;

public class MyHandler : IHostedService, IPipelineStepHandler
{
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ILogger<MyHandler> _log;


    public MyHandler(
        string stepName,
        IPipelineOrchestrator orchestrator,
        ILoggerFactory? loggerFactory = null)
    {
        StepName = stepName;
        _orchestrator = orchestrator;
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<MyHandler>();

        _log.LogInformation("Instantiating handler {0}...", GetType().FullName);
    }


    /// <inheritdoc />
    public string StepName { get; }


    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Starting handler {0}...", GetType().FullName);
        return _orchestrator.AddHandlerAsync(this, cancellationToken);
    }


    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Stopping handler {0}...", GetType().FullName);
        return _orchestrator.StopAllPipelinesAsync();
    }


    /// <inheritdoc />
    public async Task<(ReturnType returnType, DataPipeline updatedPipeline)> InvokeAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
        /* ... your custom ...
         * ... handler ...
         * ... business logic ... */

        _log.LogInformation("Running handler {0}...", GetType().FullName);

        // Remove this - here only to avoid build errors
        await Task.Delay(0, cancellationToken).ConfigureAwait(false);

        return (ReturnType.Success, pipeline);
    }
}
