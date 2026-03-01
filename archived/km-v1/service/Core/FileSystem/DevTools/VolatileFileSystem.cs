// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Pipeline;

namespace Microsoft.KernelMemory.FileSystem.DevTools;

/// <summary>
/// Simple file system abstraction that saves text files in memory.
/// </summary>
internal sealed class VolatileFileSystem : IFileSystem
{
    private const string DefaultVolumeName = "__default__";
    private const char DirSeparator = '/';

    private static readonly Regex s_invalidCharsRegex = new(@"[\s|\||\\|/|\0|'|\`|""|:|;|,|~|!|?|*|+|=|^|@|#|$|%|&]");

    /// <summary>
    /// To avoid collisions, singletons are split by root directory
    /// </summary>
    private static readonly ConcurrentDictionary<string, VolatileFileSystem> s_singletons = new();

    private readonly ILogger _log;
    private readonly IMimeTypeDetection _mimeTypeDetection;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BinaryData>> _volumes = new();


    /// <summary>
    /// Ctor accessible to unit tests only.
    /// </summary>
    internal VolatileFileSystem(IMimeTypeDetection? mimeTypeDetection = null, ILoggerFactory? loggerFactory = null)
    {
        _mimeTypeDetection = mimeTypeDetection ?? new MimeTypesDetection();
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<VolatileFileSystem>();
    }


    /// <summary>
    /// Note: the volatile FS should be used as a singleton, in order to share state
    /// (directories and files) across clients. E.g. the simple queue requires a shared
    /// instance to work properly.
    /// </summary>
    public static VolatileFileSystem GetInstance(string directory, IMimeTypeDetection? mimeTypeDetection = null, ILoggerFactory? loggerFactory = null)
    {
        directory = directory.Trim('/').Trim('\\').ToLowerInvariant();

        if (!s_singletons.ContainsKey(directory))
        {
            // s_singletons[directory] = new VolatileFileSystem(log);
            s_singletons.AddOrUpdate(directory,
                _ => new VolatileFileSystem(mimeTypeDetection, loggerFactory),
                (_, _) => new VolatileFileSystem(mimeTypeDetection, loggerFactory));
        }

        return s_singletons[directory];
    }


    #region Volume API

    /// <inheritdoc />
    public Task CreateVolumeAsync(string volume, CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);

        if (!_volumes.ContainsKey(volume))
        {
            _volumes.TryAdd(volume, new ConcurrentDictionary<string, BinaryData>());
        }

        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public Task<bool> VolumeExistsAsync(string volume, CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        return Task.FromResult(_volumes.ContainsKey(volume));
    }


    /// <inheritdoc />
    public Task DeleteVolumeAsync(string volume, CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        _volumes.TryRemove(volume, out _);
        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public Task<IEnumerable<string>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_volumes.Keys.Select(x => x));
    }

    #endregion


    #region Directory API

    /// <inheritdoc />
    public async Task CreateDirectoryAsync(string volume, string relPath, CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);

        await CreateVolumeAsync(volume, cancellationToken).ConfigureAwait(false);
        relPath = ValidatePath(relPath);

        // Note: the value has a '/' at the end
        var path = JoinPaths(relPath, "");

        _volumes[volume][path] = new BinaryData(string.Empty);
    }


    /// <inheritdoc />
    public async Task DeleteDirectoryAsync(string volume, string relPath, CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        relPath = ValidatePath(relPath);

        if (_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
        {
            var files = await GetAllFileNamesAsync(volume, relPath, cancellationToken).ConfigureAwait(false);

            foreach (var fileName in files)
            {
                var path = JoinPaths(relPath, fileName);
                volumeData.TryRemove(path, out _);
            }

            var dirPath = JoinPaths(relPath, "");
            volumeData.TryRemove(dirPath, out _);
        }
    }

    #endregion


    #region File API

    /// <inheritdoc />
    public async Task WriteFileAsync(
        string volume,
        string relPath,
        string fileName,
        Stream streamContent,
        CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);

        await using (streamContent.ConfigureAwait(false))
        {
            var data = new BinaryData(streamContent.ReadAllBytes());
            await ValidateVolumeExistsAsync(volume, cancellationToken).ConfigureAwait(false);

            if (!_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
            {
                ThrowVolumeNotFound(volume);
                return;
            }

            relPath = ValidatePath(relPath);
            fileName = ValidateFileName(fileName);
            var path = JoinPaths(relPath, fileName);
            volumeData[path] = data;
        }
    }


    /// <inheritdoc />
    public async Task WriteFileAsync(
        string volume,
        string relPath,
        string fileName,
        string data,
        CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        await ValidateVolumeExistsAsync(volume, cancellationToken).ConfigureAwait(false);

        if (!_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
        {
            ThrowVolumeNotFound(volume);
            return;
        }

        relPath = ValidatePath(relPath);
        fileName = ValidateFileName(fileName);
        var path = JoinPaths(relPath, fileName);
        volumeData[path] = new BinaryData(data);
    }


    /// <inheritdoc />
    public async Task<string> ReadFileAsTextAsync(
        string volume,
        string relPath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        return (await ReadFileAsBinaryAsync(volume,
                    relPath,
                    fileName,
                    cancellationToken)
                .ConfigureAwait(false))
            .ToString();
    }


    /// <inheritdoc />
    public Task<BinaryData> ReadFileAsBinaryAsync(
        string volume,
        string relPath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        BinaryData result = new(string.Empty);

        if (_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
        {
            relPath = ValidatePath(relPath);
            fileName = ValidateFileName(fileName);
            var dirPath = JoinPaths(relPath, "");
            var filePath = JoinPaths(relPath, fileName);

            if (!volumeData.Keys.Any(x => x.StartsWith(dirPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DirectoryNotFoundException($"Directory not found: {dirPath}");
            }

            if (!volumeData.TryGetValue(filePath, out result!))
            {
                _log.LogError("File not found: {0}", filePath);
                throw new FileNotFoundException($"File not found: {filePath}");
            }
        }
        else
        {
            ThrowVolumeNotFound(volume);
        }

        return Task.FromResult(result);
    }


    public Task<StreamableFileContent> ReadFileInfoAsync(
        string volume,
        string relPath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        StreamableFileContent result = new();

        if (_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
        {
            relPath = ValidatePath(relPath);
            fileName = ValidateFileName(fileName);
            var dirPath = JoinPaths(relPath, "");
            var filePath = JoinPaths(relPath, fileName);

            if (!volumeData.Keys.Any(x => x.StartsWith(dirPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DirectoryNotFoundException($"Directory not found: {dirPath}");
            }

            BinaryData file = new(string.Empty);

            if (!volumeData.TryGetValue(filePath, out file!))
            {
                _log.LogError("File not found: {0}", filePath);
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var fileType = _mimeTypeDetection.GetFileType(fileName);
            Task<Stream> AsyncStreamDelegate() => Task.FromResult<Stream>(file.ToStream());
            result = new StreamableFileContent(fileName,
                file.Length,
                fileType,
                DateTime.UtcNow,
                AsyncStreamDelegate);
        }
        else
        {
            ThrowVolumeNotFound(volume);
        }

        return Task.FromResult<StreamableFileContent>(result);
    }


    /// <inheritdoc />
    public Task<IEnumerable<string>> GetAllFileNamesAsync(string volume, string relPath, CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        relPath = ValidatePath(relPath);
        var result = new List<string>();

        if (_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
        {
            // the value has a "/" at the end to correctly check for prefix
            var path = JoinPaths(relPath, "");
            // find all files starting with the prefix, excluding dirs
            result.AddRange(from entry in volumeData
                            where entry.Key.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                                && entry.Key != path
                                && !entry.Key.Substring(path.Length).Contains('/', StringComparison.Ordinal)
                            select entry.Key.Substring(path.Length));
        }
        else
        {
            ThrowVolumeNotFound(volume);
        }

        return Task.FromResult((IEnumerable<string>)result);
    }


    /// <inheritdoc />
    public Task<bool> FileExistsAsync(
        string volume,
        string relPath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        relPath = ValidatePath(relPath);
        var path = JoinPaths(relPath, fileName);

        try
        {
            return Task.FromResult(_volumes.ContainsKey(volume)
                && _volumes[volume].ContainsKey(path)
                && !path.EndsWith($"{DirSeparator}", StringComparison.Ordinal));
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult(false);
        }
    }


    /// <inheritdoc />
    public Task DeleteFileAsync(
        string volume,
        string relPath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        volume = ValidateVolumeName(volume);
        relPath = ValidatePath(relPath);

        if (_volumes.TryGetValue(volume, out var volumeData))
        {
            var path = JoinPaths(relPath, fileName);
            volumeData.TryRemove(path, out _);
        }

        return Task.CompletedTask;
    }


    /// <inheritdoc />
    public Task<IDictionary<string, string>> ReadAllFilesAsTextAsync(string volume, string relPath, CancellationToken cancellationToken = default)
    {
        IDictionary<string, string> result = new Dictionary<string, string>();

        volume = ValidateVolumeName(volume);
        relPath = ValidatePath(relPath);

        if (_volumes.TryGetValue(volume, out ConcurrentDictionary<string, BinaryData>? volumeData))
        {
            // add "/" at the end to correctly check for prefix
            var path = JoinPaths(relPath, "");

            foreach (KeyValuePair<string, BinaryData> entry in volumeData)
            {
                if (entry.Key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new KeyValuePair<string, string>(entry.Key, entry.Value.ToString()));
                }
            }
        }

        return Task.FromResult(result);
    }

    #endregion


    #region private

    internal ConcurrentDictionary<string, ConcurrentDictionary<string, BinaryData>> GetInternalState()
    {
        return _volumes;
    }


    // ReSharper disable once InconsistentNaming
    private Task ValidateVolumeExistsAsync(string volume, CancellationToken cancellationToken)
    {
        if (!_volumes.ContainsKey(volume))
        {
            ThrowVolumeNotFound(volume);
        }

        return Task.CompletedTask;
    }


    private void ThrowVolumeNotFound(string volume)
    {
        // Don't log errors here, this can be expected, let the caller handle the exception
        throw new DirectoryNotFoundException($"Volume not found: {volume}");
    }


    private static string ValidateVolumeName(string volume)
    {
        if (string.IsNullOrEmpty(volume))
        {
            return DefaultVolumeName;
        }

        if (s_invalidCharsRegex.Match(volume).Success)
        {
            throw new ArgumentException($"The volume name '{volume}' contains some invalid chars or empty spaces");
        }

        return volume;
    }


    private static string ValidatePath(string path)
    {
        // Check invalid chars one at a time for better error messages
        if (path.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("The path contains some invalid chars: backslash '\\' chars are not allowed");
        }

        if (path.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("The path contains some invalid chars: colon ':' chars are not allowed");
        }

        return path;
    }


    private static string ValidateFileName(string fileName)
    {
        // Check invalid chars one at a time for better error messages
        if (fileName.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException($"The file name {fileName} contains some invalid chars: slash '/' chars are not allowed");
        }

        if (fileName.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException($"The file name {fileName} contains some invalid chars: backslash '\\' chars are not allowed");
        }

        if (fileName.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"The file name {fileName} contains some invalid chars: colon ':' chars are not allowed");
        }

        return fileName;
    }


    private static string JoinPaths(string a, string b)
    {
        return $"{a.Trim('/').Trim('\\')}{DirSeparator}{b.Trim('/').Trim('\\')}";
    }

    #endregion


}
