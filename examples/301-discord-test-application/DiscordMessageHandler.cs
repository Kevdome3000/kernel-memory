// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using _301_discord_test_application.DiscordConnector;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;

namespace _301_discord_test_application;

/// <summary>
/// KM pipeline handler fetching discord data files from document storage
/// and storing messages in Postgres.
/// </summary>
public sealed class DiscordMessageHandler : IPipelineStepHandler, IDisposable
{
    // Name of the file where to store Discord data
    private readonly string _filename;

    // KM pipelines orchestrator
    private readonly IPipelineOrchestrator _orchestrator;

    // .NET service provider, used to get thread safe instances of EF DbContext
    private readonly IServiceProvider _serviceProvider;

    // DB creation
    private readonly object _dbCreation = new();
    private bool _dbCreated = false;
    private bool _useScope = false;
    private readonly IServiceScope _dbScope;

    // .NET logger
    private readonly ILogger<DiscordMessageHandler> _log;

    public string StepName { get; } = string.Empty;


    public DiscordMessageHandler(
        string stepName,
        IPipelineOrchestrator orchestrator,
        DiscordConnectorConfig config,
        IServiceProvider serviceProvider,
        ILoggerFactory? loggerFactory = null)
    {
        StepName = stepName;
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<DiscordMessageHandler>();

        _orchestrator = orchestrator;
        _serviceProvider = serviceProvider;
        _filename = config.FileName;
        _dbScope = _serviceProvider.CreateScope();

        try
        {
            OnFirstInvoke();
        }
        catch (Exception)
        {
            // ignore, will retry later
        }
    }


    public async Task<(ReturnType returnType, DataPipeline updatedPipeline)> InvokeAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
        OnFirstInvoke();

        // Note: use a new DbContext instance each time, because DbContext is not thread safe and would throw the following
        // exception: System.InvalidOperationException: a second operation was started on this context instance before a previous
        // operation completed. This is usually caused by different threads concurrently using the same instance of DbContext.
        // For more information on how to avoid threading issues with DbContext, see https://go.microsoft.com/fwlink/?linkid=2097913.
        var db = GetDb();

        await using (db.ConfigureAwait(false))
        {
            foreach (DataPipeline.FileDetails uploadedFile in pipeline.Files)
            {
                // Process only the file containing the discord data
                if (uploadedFile.Name != _filename) { continue; }

                string fileContent = await _orchestrator.ReadTextFileAsync(pipeline, uploadedFile.Name, cancellationToken).ConfigureAwait(false);

                DiscordDbMessage? data;

                try
                {
                    data = JsonSerializer.Deserialize<DiscordDbMessage>(fileContent);

                    if (data == null)
                    {
                        _log.LogError("Failed to deserialize Discord data file, result is NULL");
                        return (ReturnType.FatalError, pipeline);
                    }
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Failed to deserialize Discord data file");
                    return (ReturnType.FatalError, pipeline);
                }

                await db.Messages.AddAsync(data, cancellationToken).ConfigureAwait(false);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return (ReturnType.Success, pipeline);
    }


    public void Dispose()
    {
        _dbScope.Dispose();
    }


    private void OnFirstInvoke()
    {
        if (_dbCreated) { return; }

        lock (_dbCreation)
        {
            if (_dbCreated) { return; }

            var db = GetDb();
            db.Database.EnsureCreated();
            db.Dispose();
            db = null;

            _dbCreated = true;

            _log.LogInformation("DB created");
        }
    }


    /// <summary>
    /// Depending on the hosting type, the DB Context is retrieved in different ways.
    /// Single host app:
    ///     db = _serviceProvider.GetService[DiscordDbContext](); // this throws an exception in multi-host mode
    /// Multi host app:
    ///     db = serviceProvider.CreateScope().ServiceProvider.GetRequiredService[DiscordDbContext]();
    /// </summary>
    private DiscordDbContext GetDb()
    {
        DiscordDbContext? db;

        if (_useScope)
        {
            db = _dbScope.ServiceProvider.GetRequiredService<DiscordDbContext>();
        }
        else
        {
            try
            {
                // Try the single app host first
                _log.LogTrace("Retrieving Discord DB context using service provider");
                db = _serviceProvider.GetService<DiscordDbContext>();
            }
            catch (InvalidOperationException)
            {
                // If the single app host fails, try the multi app host
                _log.LogInformation("Retrieving Discord DB context using scope");
                db = _dbScope.ServiceProvider.GetRequiredService<DiscordDbContext>();

                // If the multi app host succeeds, set a flag to remember to use the scope
                if (db != null)
                {
                    _useScope = true;
                }
            }
        }

        ArgumentNullExceptionEx.ThrowIfNull(db, nameof(db), "Discord DB context is NULL");

        return db;
    }
}
