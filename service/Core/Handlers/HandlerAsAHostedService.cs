// Copyright (c) Microsoft.All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;

#pragma warning disable IDE0130 // reduce number of "using" statements
// ReSharper disable once CheckNamespace - reduce number of "using" statements
namespace Microsoft.KernelMemory;

/// <summary>
/// Wrapper of handler classes, allowing to run handlers as services hosted by IHost
/// </summary>
/// <typeparam name="T">Handler class</typeparam>
public sealed class HandlerAsAHostedService<T> : IHostedService where T : IPipelineStepHandler
{
    private readonly T _handler;
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly string _stepName;
    private readonly ILogger<HandlerAsAHostedService<T>> _log;


    public HandlerAsAHostedService(
        string stepName,
        IPipelineOrchestrator orchestrator,
        T handler,
        ILoggerFactory? loggerFactory = null)
    {
        _stepName = stepName;
        _orchestrator = orchestrator;
        _handler = handler;

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<HandlerAsAHostedService<T>>();
        _log.LogInformation("Handler as service created: {0}", stepName);
    }


    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Handler service started: {0}", _stepName);
        return _orchestrator.AddHandlerAsync(_handler, cancellationToken);
    }


    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Stopping handler service: {0}", _stepName);
        return _orchestrator.StopAllPipelinesAsync();
    }
}
