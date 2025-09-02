// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.DocumentStorage.AWSS3;

public sealed class AWSS3Storage : IDocumentStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly ILogger<AWSS3Storage> _log;
    private readonly string _bucketName;


    public AWSS3Storage(
        AWSS3Config config,
        ILogger<AWSS3Storage>? log = null)
    {
        config.Validate();

        _log = log ?? DefaultLogger<AWSS3Storage>.Instance;
        _bucketName = config.BucketName;

        switch (config.Auth)
        {
            case AWSS3Config.AuthTypes.AccessKey:
            {
                _client = new AmazonS3Client(
                    config.AccessKey,
                    config.SecretAccessKey,
                    new AmazonS3Config
                    {
                        ForcePathStyle = config.ForcePathStyle,
                        ServiceURL = config.Endpoint,
                        LogResponse = true
                    }
                );
                break;
            }
            case AWSS3Config.AuthTypes.CredentialChain:
            {
                _client = new AmazonS3Client(new AmazonS3Config
                {
                    ForcePathStyle = config.ForcePathStyle,
                    ServiceURL = config.Endpoint,
                    LogResponse = true
                });
                break;
            }

            default:
                _log.LogCritical("Authentication type '{0}' undefined or not supported", config.Auth);
                throw new DocumentStorageException($"Authentication type '{config.Auth}' undefined or not supported");
        }
    }


    /// <inheritdoc />
    public Task CreateIndexDirectoryAsync(
        string index,
        CancellationToken cancellationToken = default)
    {
        // Note: AWS S3 doesn't have an artifact for "directories", which are just a detail
        //       in an object name so there's no such thing as creating a directory.
        //       For example, if you want to create a directory called "images" within a bucket,
        //       you can set the object key as "images/object.jpg". This would give the appearance
        //       of a file being stored in the "images" directory.

        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public async Task DeleteIndexDirectoryAsync(string index, CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Deleting index '{0}'", index);

        if (string.IsNullOrWhiteSpace(index))
        {
            throw new DocumentStorageException("The index name is empty, stopping the process to prevent data loss");
        }

        await DeleteObjectsByPrefixAsync(index, cancellationToken).ConfigureAwait(false);
    }


    /// <inheritdoc />
    public Task CreateDocumentDirectoryAsync(
        string index,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        // Note: AWS S3 doesn't have an artifact for "directories", which are just a detail in a blob name
        //       so there's no such thing as creating a directory. When creating a blob, the name must contain
        //       the directory name, e.g. blob.Name = "dir1/subdir2/file.txt"
        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public async Task EmptyDocumentDirectoryAsync(string index, string documentId, CancellationToken cancellationToken = default)
    {
        var directoryName = $"{index}/{documentId}";

        if (string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(directoryName))
        {
            throw new DocumentStorageException("The index, or document ID, or directory name is empty, stopping the process to prevent data loss");
        }

        await DeleteObjectsByPrefixAsync(directoryName, cancellationToken).ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task DeleteDocumentDirectoryAsync(
        string index,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var directoryName = $"{index}/{documentId}";

        if (string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(directoryName))
        {
            throw new DocumentStorageException("The index, or document ID, or directory name is empty, stopping the process to prevent data loss");
        }

        await DeleteObjectsByPrefixAsync(directoryName, cancellationToken).ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task WriteFileAsync(
        string index,
        string documentId,
        string fileName,
        Stream streamContent,
        CancellationToken cancellationToken = default)
    {
        var objectKey = $"{index}/{documentId}/{fileName}";
        var len = streamContent.Length;

        _log.LogTrace("Writing object {0} ...", objectKey);

        if (streamContent.Length == 0)
        {
            _log.LogWarning("The file {0} is empty", objectKey);
        }

        await _client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = objectKey,
                    InputStream = streamContent
                },
                cancellationToken)
            .ConfigureAwait(false);

        _log.LogTrace("Object {0} ready, size {1}", objectKey, len);
    }


    /// <inheritdoc />
    public async Task<StreamableFileContent> ReadFileAsync(
        string index,
        string documentId,
        string fileName,
        bool logErrIfNotFound = true,
        CancellationToken cancellationToken = default)
    {
        var objectKey = $"{index}/{documentId}/{fileName}";

        try
        {
            GetObjectRequest request = new() { BucketName = _bucketName, Key = objectKey };
            var response = await _client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);

            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);

            return new StreamableFileContent(
                fileName,
                response.ContentLength,
                response.Headers.ContentType,
                (DateTimeOffset)response.LastModified,
                () =>
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    return Task.FromResult((Stream)memoryStream);
                }
            );
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            if (logErrIfNotFound)
            {
                _log.LogInformation("File not found: {0}", objectKey);
            }

            throw new DocumentStorageFileNotFoundException("File not found", e);
        }
    }


    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }


    #region private

    private async Task DeleteObjectsByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new DocumentStorageException("The object prefix is empty, stopping the process to prevent data loss");
        }

        _log.LogTrace("Deleting objects with prefix '{0}'", prefix);

        var allObjects = new List<S3Object>();
        var request = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = prefix
        };

        do
        {
            ListObjectsV2Response? response = await _client.ListObjectsV2Async(
                    request,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (response == null) { break; }

            allObjects.AddRange(response.S3Objects);

            if ((bool)!response.IsTruncated)
            {
                // Exit the loop if there are no more objects to retrieve
                break;
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (true);

        foreach (var obj in allObjects)
        {
            var fileName = obj.Key.Trim('/').Substring(prefix.Trim('/').Length).Trim('/');

            // Don't delete the pipeline status file
            if (fileName == Constants.PipelineStatusFilename) { continue; }

            _log.LogInformation("Deleting blob '{0}', filename '{1}' from bucket '{2}'",
                obj.Key,
                fileName,
                _bucketName);

            var response = await _client.DeleteObjectAsync(
                    _bucketName,
                    obj.Key,
                    cancellationToken
                )
                .ConfigureAwait(false);

            // 204 No Content: This status code indicates that the object was successfully deleted
            // from the bucket. The request was processed successfully, and there is no content
            // to return in the response.
            if (response.HttpStatusCode == HttpStatusCode.NoContent)
            {
                _log.LogDebug("Delete response: {0}", response.HttpStatusCode);
            }
            else
            {
                _log.LogWarning("Unexpected delete response: {0}", response.HttpStatusCode);
            }
        }
    }

    #endregion


}
