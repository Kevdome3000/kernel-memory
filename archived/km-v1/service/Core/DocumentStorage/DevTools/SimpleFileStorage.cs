// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Pipeline;

namespace Microsoft.KernelMemory.DocumentStorage.DevTools;

[Experimental("KMEXP03")]
public class SimpleFileStorage : IDocumentStorage
{
    private readonly ILogger<SimpleFileStorage> _log;
    private readonly IFileSystem _fileSystem;


    public SimpleFileStorage(
        SimpleFileStorageConfig config,
        IMimeTypeDetection? mimeTypeDetection = null,
        ILoggerFactory? loggerFactory = null)
    {
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<SimpleFileStorage>();

        switch (config.StorageType)
        {
            case FileSystemTypes.Disk:
                _fileSystem = new DiskFileSystem(config.Directory, mimeTypeDetection, loggerFactory);
                break;

            case FileSystemTypes.Volatile:
                _fileSystem = VolatileFileSystem.GetInstance(config.Directory, mimeTypeDetection, loggerFactory);
                break;

            default:
                throw new ArgumentException($"Unknown storage type {config.StorageType}");
        }
    }


    /// <inheritdoc />
    public Task CreateIndexDirectoryAsync(string index, CancellationToken cancellationToken = default)
    {
        return _fileSystem.CreateVolumeAsync(index, cancellationToken);
    }


    /// <inheritdoc />
    public Task DeleteIndexDirectoryAsync(string index, CancellationToken cancellationToken = default)
    {
        return _fileSystem.DeleteVolumeAsync(index, cancellationToken);
    }


    /// <inheritdoc />
    public Task CreateDocumentDirectoryAsync(
        string index,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return _fileSystem.CreateDirectoryAsync(index, documentId, cancellationToken);
    }


    /// <inheritdoc />
    public async Task EmptyDocumentDirectoryAsync(
        string index,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var files = await _fileSystem.GetAllFileNamesAsync(index, documentId, cancellationToken).ConfigureAwait(false);

        foreach (string fileName in files)
        {
            // Don't delete the pipeline status file
            if (fileName == Constants.PipelineStatusFilename) { continue; }

            await _fileSystem.DeleteFileAsync(index,
                    documentId,
                    fileName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }


    /// <inheritdoc />
    public Task DeleteDocumentDirectoryAsync(
        string index,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        return _fileSystem.DeleteDirectoryAsync(index, documentId, cancellationToken);
    }


    /// <inheritdoc />
    public async Task WriteFileAsync(
        string index,
        string documentId,
        string fileName,
        Stream streamContent,
        CancellationToken cancellationToken = default)
    {
        await _fileSystem.CreateDirectoryAsync(index, documentId, cancellationToken).ConfigureAwait(false);
        await _fileSystem.WriteFileAsync(index,
                documentId,
                fileName,
                streamContent,
                cancellationToken)
            .ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task<StreamableFileContent> ReadFileAsync(
        string index,
        string documentId,
        string fileName,
        bool logErrIfNotFound = true,
        CancellationToken cancellationToken = default)
    {
        // IMPORTANT: documentId can be empty, e.g. when deleting an index
        ArgumentNullExceptionEx.ThrowIfNullOrEmpty(index, nameof(index), "Index name is empty");
        ArgumentNullExceptionEx.ThrowIfNullOrEmpty(fileName, nameof(fileName), "Filename is empty");

        try
        {
            return await _fileSystem.ReadFileInfoAsync(index,
                    documentId,
                    fileName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException)
        {
            if (logErrIfNotFound)
            {
                _log.LogError("File not found {0}/{1}/{2}",
                    index,
                    documentId,
                    fileName);
            }

            throw new DocumentStorageFileNotFoundException("File not found");
        }
    }
}
