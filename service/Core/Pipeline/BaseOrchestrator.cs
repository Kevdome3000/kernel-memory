// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.DocumentStorage;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.Pipeline;

[Experimental("KMEXP04")]
public abstract class BaseOrchestrator : IPipelineOrchestrator, IDisposable
{
    private static readonly JsonSerializerOptions s_indentedJsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions s_notIndentedJsonOptions = new() { WriteIndented = false };

    private readonly List<IMemoryDb> _memoryDbs;
    private readonly List<ITextEmbeddingGenerator> _embeddingGenerators;
    private readonly ITextGenerator _textGenerator;
    private readonly List<string> _defaultIngestionSteps;
    private readonly IDocumentStorage _documentStorage;
    private readonly IMimeTypeDetection _mimeTypeDetection;
    private readonly string? _defaultIndexName;

    protected ILogger<BaseOrchestrator> Log { get; private set; }
    protected CancellationTokenSource CancellationTokenSource { get; private set; }


    protected BaseOrchestrator(
        IDocumentStorage documentStorage,
        List<ITextEmbeddingGenerator> embeddingGenerators,
        List<IMemoryDb> memoryDbs,
        ITextGenerator textGenerator,
        IMimeTypeDetection? mimeTypeDetection = null,
        KernelMemoryConfig? config = null,
        ILogger<BaseOrchestrator>? log = null)
    {
        config ??= new KernelMemoryConfig();

        Log = log ?? DefaultLogger<BaseOrchestrator>.Instance;
        _defaultIngestionSteps = config.DataIngestion.GetDefaultStepsOrDefaults();
        EmbeddingGenerationEnabled = config.DataIngestion.EmbeddingGenerationEnabled;
        _documentStorage = documentStorage;
        _embeddingGenerators = embeddingGenerators;
        _memoryDbs = memoryDbs;
        _textGenerator = textGenerator;
        _defaultIndexName = config?.DefaultIndexName;

        _mimeTypeDetection = mimeTypeDetection ?? new MimeTypesDetection();
        CancellationTokenSource = new CancellationTokenSource();

        if (EmbeddingGenerationEnabled && embeddingGenerators.Count == 0)
        {
            Log.LogWarning("No embedding generators available");
        }

        if (memoryDbs.Count == 0)
        {
            Log.LogWarning("No vector DBs available");
        }
    }


    ///<inheritdoc />
    public abstract List<string> HandlerNames { get; }


    ///<inheritdoc />
    public abstract Task AddHandlerAsync(IPipelineStepHandler handler, CancellationToken cancellationToken = default);


    ///<inheritdoc />
    public abstract Task TryAddHandlerAsync(IPipelineStepHandler handler, CancellationToken cancellationToken = default);


    ///<inheritdoc />
    public abstract Task RunPipelineAsync(DataPipeline pipeline, CancellationToken cancellationToken = default);


    ///<inheritdoc />
    public async Task<string> ImportDocumentAsync(
        string index,
        DocumentUploadRequest uploadRequest,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        Log.LogInformation("Queueing upload of {0} files for further processing [request {1}]", uploadRequest.Files.Count, uploadRequest.DocumentId);

        index = IndexName.CleanName(index, _defaultIndexName);

        var pipeline = PrepareNewDocumentUpload(
            index,
            uploadRequest.DocumentId,
            uploadRequest.Tags,
            uploadRequest.Files,
            context?.Arguments);

        if (uploadRequest.Steps.Count > 0)
        {
            foreach (var step in uploadRequest.Steps)
            {
                pipeline.Then(step);
            }
        }
        else
        {
            foreach (var step in _defaultIngestionSteps)
            {
                pipeline.Then(step);
            }
        }

        pipeline.Build();

        try
        {
            await RunPipelineAsync(pipeline, cancellationToken).ConfigureAwait(false);
            return pipeline.DocumentId;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Pipeline start failed.");
            throw;
        }
    }


    ///<inheritdoc />
    public DataPipeline PrepareNewDocumentUpload(
        string index,
        string documentId,
        TagCollection tags,
        IEnumerable<DocumentUploadRequest.UploadedFile>? filesToUpload = null,
        IDictionary<string, object?>? contextArgs = null)
    {
        index = IndexName.CleanName(index, _defaultIndexName);

        filesToUpload ??= [];

        var pipeline = new DataPipeline
        {
            Index = index,
            DocumentId = documentId,
            Tags = tags,
            ContextArguments = contextArgs ?? new Dictionary<string, object?>(),
            FilesToUpload = filesToUpload.ToList()
        };

        pipeline.Validate();

        return pipeline;
    }


    ///<inheritdoc />
    public async Task<DataPipeline?> ReadPipelineStatusAsync(string index, string documentId, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);

        try
        {
            using StreamableFileContent? streamableContent = await _documentStorage.ReadFileAsync(index,
                    documentId,
                    Constants.PipelineStatusFilename,
                    false,
                    cancellationToken)
                .ConfigureAwait(false);

            if (streamableContent == null)
            {
                throw new InvalidPipelineDataException("The pipeline data is not found");
            }

            BinaryData? content = await BinaryData.FromStreamAsync(await streamableContent.GetStreamAsync().ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);

            if (content == null)
            {
                throw new InvalidPipelineDataException("The pipeline data is null");
            }

            var result = JsonSerializer.Deserialize<DataPipeline>(content.ToString().RemoveBOM().Trim());

            if (result == null)
            {
                throw new InvalidPipelineDataException("The pipeline data deserializes to a null value");
            }

            return result;
        }
        catch (DocumentStorageFileNotFoundException)
        {
            throw new PipelineNotFoundException("Pipeline/Document not found");
        }
    }


    ///<inheritdoc />
    public async Task<DataPipelineStatus?> ReadPipelineSummaryAsync(string index, string documentId, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);

        try
        {
            DataPipeline? pipeline = await ReadPipelineStatusAsync(index, documentId, cancellationToken).ConfigureAwait(false);
            return pipeline?.ToDataPipelineStatus();
        }
        catch (PipelineNotFoundException)
        {
            return null;
        }
    }


    ///<inheritdoc />
    public async Task<bool> IsDocumentReadyAsync(string index, string documentId, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);

        try
        {
            Log.LogDebug("Checking if document {Id} on index {Index} is ready", documentId, index);
            DataPipeline? pipeline = await ReadPipelineStatusAsync(index, documentId, cancellationToken).ConfigureAwait(false);

            if (pipeline == null)
            {
                Log.LogWarning("Document {Id} on index {Index} is not ready, pipeline is NULL", documentId, index);
                return false;
            }

            Log.LogDebug("Document {Id} on index {Index}, Complete = {Complete}, Files Count = {Count}",
                documentId,
                index,
                pipeline.Complete,
                pipeline.Files.Count);
            return pipeline.Complete && pipeline.Files.Count > 0;
        }
        catch (PipelineNotFoundException)
        {
            Log.LogWarning("Document {Id} on index {Index} not found", documentId, index);
            return false;
        }
    }


    ///<inheritdoc />
    public Task StopAllPipelinesAsync()
    {
        return CancellationTokenSource.CancelAsync();
    }


    ///<inheritdoc />
    public async Task<StreamableFileContent> ReadFileAsStreamAsync(DataPipeline pipeline, string fileName, CancellationToken cancellationToken = default)
    {
        pipeline.Index = IndexName.CleanName(pipeline.Index, _defaultIndexName);
        return await _documentStorage.ReadFileAsync(pipeline.Index,
                pipeline.DocumentId,
                fileName,
                true,
                cancellationToken)
            .ConfigureAwait(false);
    }


    ///<inheritdoc />
    public async Task<string> ReadTextFileAsync(DataPipeline pipeline, string fileName, CancellationToken cancellationToken = default)
    {
        pipeline.Index = IndexName.CleanName(pipeline.Index, _defaultIndexName);
        return (await ReadFileAsync(pipeline, fileName, cancellationToken).ConfigureAwait(false)).ToString();
    }


    ///<inheritdoc />
    public async Task<BinaryData> ReadFileAsync(DataPipeline pipeline, string fileName, CancellationToken cancellationToken = default)
    {
        using StreamableFileContent streamableContent = await ReadFileAsStreamAsync(pipeline, fileName, cancellationToken).ConfigureAwait(false);
        return await BinaryData.FromStreamAsync(await streamableContent.GetStreamAsync().ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
    }


    ///<inheritdoc />
    public Task WriteTextFileAsync(
        DataPipeline pipeline,
        string fileName,
        string fileContent,
        CancellationToken cancellationToken = default)
    {
        pipeline.Index = IndexName.CleanName(pipeline.Index, _defaultIndexName);
        return WriteFileAsync(pipeline,
            fileName,
            new BinaryData(fileContent),
            cancellationToken);
    }


    ///<inheritdoc />
    public Task WriteFileAsync(
        DataPipeline pipeline,
        string fileName,
        BinaryData fileContent,
        CancellationToken cancellationToken = default)
    {
        pipeline.Index = IndexName.CleanName(pipeline.Index, _defaultIndexName);
        return _documentStorage.WriteFileAsync(pipeline.Index,
            pipeline.DocumentId,
            fileName,
            fileContent.ToStream(),
            cancellationToken);
    }


    ///<inheritdoc />
    public bool EmbeddingGenerationEnabled { get; }


    ///<inheritdoc />
    public List<ITextEmbeddingGenerator> GetEmbeddingGenerators()
    {
        return _embeddingGenerators;
    }


    ///<inheritdoc />
    public List<IMemoryDb> GetMemoryDbs()
    {
        return _memoryDbs;
    }


    ///<inheritdoc />
    public ITextGenerator GetTextGenerator()
    {
        return _textGenerator;
    }


    ///<inheritdoc />
    public Task StartIndexDeletionAsync(string? index = null, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);
        DataPipeline pipeline = PrepareIndexDeletion(index);
        return RunPipelineAsync(pipeline, cancellationToken);
    }


    ///<inheritdoc />
    public Task StartDocumentDeletionAsync(string documentId, string? index = null, CancellationToken cancellationToken = default)
    {
        index = IndexName.CleanName(index, _defaultIndexName);
        DataPipeline pipeline = PrepareDocumentDeletion(index, documentId);
        return RunPipelineAsync(pipeline, cancellationToken);
    }


    ///<inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    /// <summary>
    /// If the pipeline asked to delete a document or an index, there might be some files
    /// left over in the storage, such as the status file that we wish to delete to keep
    /// the storage clean. We try to delete what is left, ignoring exceptions.
    /// </summary>
    protected async Task CleanUpAfterCompletionAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
    {
#pragma warning disable CA1031 // catch all by design
        if (pipeline.IsDocumentDeletionPipeline())
        {
            try
            {
                await _documentStorage.DeleteDocumentDirectoryAsync(pipeline.Index, pipeline.DocumentId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.LogError(e, "Error while trying to delete the document directory.");
            }
        }

        if (pipeline.IsIndexDeletionPipeline())
        {
            try
            {
                await _documentStorage.DeleteIndexDirectoryAsync(pipeline.Index, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.LogError(e, "Error while trying to delete the index directory.");
            }
        }
#pragma warning restore CA1031
    }


    protected static DataPipeline PrepareIndexDeletion(string? index)
    {
        var pipeline = new DataPipeline
        {
            Index = index!,
            DocumentId = string.Empty
        };

        return pipeline.Then(Constants.PipelineStepsDeleteIndex).Build();
    }


    protected static DataPipeline PrepareDocumentDeletion(string? index, string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new KernelMemoryException("The document ID is empty");
        }

        var pipeline = new DataPipeline
        {
            Index = index!,
            DocumentId = documentId
        };

        return pipeline.Then(Constants.PipelineStepsDeleteDocument).Build();
    }


    protected async Task UploadFilesAsync(DataPipeline currentPipeline, CancellationToken cancellationToken = default)
    {
        if (currentPipeline.UploadComplete)
        {
            Log.LogDebug("Upload complete");
            return;
        }

        // If the folder contains the status of a previous execution,
        // capture it to run consolidation later, e.g. purging deprecated memory records.
        // Note: although not required, the list of executions to purge is ordered from oldest to most recent
        DataPipeline? previousPipeline;

        try
        {
            previousPipeline = await ReadPipelineStatusAsync(currentPipeline.Index, currentPipeline.DocumentId, cancellationToken).ConfigureAwait(false);
        }
        catch (PipelineNotFoundException)
        {
            previousPipeline = null;
        }

        if (previousPipeline != null && previousPipeline.ExecutionId != currentPipeline.ExecutionId)
        {
            var dedupe = new HashSet<string>();

            foreach (var oldExecution in currentPipeline.PreviousExecutionsToPurge)
            {
                dedupe.Add(oldExecution.ExecutionId);
            }

            foreach (var oldExecution in previousPipeline.PreviousExecutionsToPurge)
            {
                if (dedupe.Contains(oldExecution.ExecutionId)) { continue; }

                // Reset the list to avoid wasting space with nested trees
                oldExecution.PreviousExecutionsToPurge = [];

                currentPipeline.PreviousExecutionsToPurge.Add(oldExecution);
                dedupe.Add(oldExecution.ExecutionId);
            }

            // Reset the list to avoid wasting space with nested trees
            previousPipeline.PreviousExecutionsToPurge = [];

            currentPipeline.PreviousExecutionsToPurge.Add(previousPipeline);
        }

        await UploadFormFilesAsync(currentPipeline, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Update the status file, throwing an exception if the write fails.
    /// </summary>
    /// <param name="pipeline">Pipeline data</param>
    /// <param name="cancellationToken">Task cancellation token</param>
    protected async Task UpdatePipelineStatusAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        Log.LogDebug("Saving pipeline status to '{0}/{1}/{2}'",
            pipeline.Index,
            pipeline.DocumentId,
            Constants.PipelineStatusFilename);

        try
        {
            await _documentStorage.WriteFileAsync(
                    pipeline.Index,
                    pipeline.DocumentId,
                    Constants.PipelineStatusFilename,
                    new BinaryData(ToJson(pipeline, true)).ToStream(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.LogWarning(e, "Unable to save pipeline status");
            throw;
        }
    }


    protected static string ToJson(object data, bool indented = false)
    {
        return JsonSerializer.Serialize(data,
            indented
                ? s_indentedJsonOptions
                : s_notIndentedJsonOptions);
    }


    private async Task UploadFormFilesAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        Log.LogDebug("Uploading {0} files, pipeline '{1}/{2}'",
            pipeline.FilesToUpload.Count,
            pipeline.Index,
            pipeline.DocumentId);

        await _documentStorage.CreateIndexDirectoryAsync(pipeline.Index, cancellationToken).ConfigureAwait(false);
        await _documentStorage.CreateDocumentDirectoryAsync(pipeline.Index, pipeline.DocumentId, cancellationToken).ConfigureAwait(false);

        foreach (DocumentUploadRequest.UploadedFile file in pipeline.FilesToUpload)
        {
            if (string.Equals(file.FileName, Constants.PipelineStatusFilename, StringComparison.OrdinalIgnoreCase))
            {
                Log.LogError("Invalid file name, upload not supported: {0}", file.FileName);
                continue;
            }

            // Read the value before the stream is closed (would throw an exception otherwise)
            var fileSize = file.FileContent.Length;

            Log.LogDebug("Uploading file '{0}', size {1} bytes", file.FileName, fileSize);
            await _documentStorage.WriteFileAsync(pipeline.Index,
                    pipeline.DocumentId,
                    file.FileName,
                    file.FileContent,
                    cancellationToken)
                .ConfigureAwait(false);

            string mimeType = string.Empty;

            try
            {
                mimeType = _mimeTypeDetection.GetFileType(file.FileName);
            }
            catch (MimeTypeException)
            {
                Log.LogWarning("File type not supported, the ingestion pipeline might skip it");
            }

            pipeline.Files.Add(new DataPipeline.FileDetails
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = file.FileName,
                Size = fileSize,
                MimeType = mimeType,
                Tags = pipeline.Tags
            });

            Log.LogInformation("File uploaded: {0}, {1} bytes", file.FileName, fileSize);
            pipeline.LastUpdate = DateTimeOffset.UtcNow;
        }

        await UpdatePipelineStatusAsync(pipeline, cancellationToken).ConfigureAwait(false);
    }


    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancellationTokenSource.Dispose();
        }
    }
}
