// Copyright (c) Microsoft.All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.KernelMemory.Models;

public sealed class StreamableFileContent : IDisposable
{
    private Stream? _stream;

    public string FileName { get; } = string.Empty;
    public long FileSize { get; } = 0;
    public string FileType { get; } = string.Empty;
    public DateTimeOffset LastWrite { get; } = default;
    public Func<Task<Stream>> GetStreamAsync { get; }


    public StreamableFileContent()
    {
        GetStreamAsync = () => Task.FromResult<Stream>(new MemoryStream());
    }


    public StreamableFileContent(
        string fileName,
        long fileSize,
        string fileType = "application/octet-stream",
        DateTimeOffset lastWriteTimeUtc = default,
        Func<Task<Stream>>? asyncStreamDelegate = null)
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(fileType, nameof(fileType), "File content type is empty");
        ArgumentNullExceptionEx.ThrowIfNull(lastWriteTimeUtc, nameof(lastWriteTimeUtc), "File last write time is NULL");
        ArgumentNullExceptionEx.ThrowIfNull(asyncStreamDelegate, nameof(asyncStreamDelegate), "asyncStreamDelegate is NULL");

        FileName = fileName;
        FileSize = fileSize;
        FileType = fileType;
        LastWrite = lastWriteTimeUtc;
        GetStreamAsync = async () =>
        {
            _stream = await asyncStreamDelegate().ConfigureAwait(false);
            return _stream;
        };
    }


    public void Dispose()
    {
        if (_stream == null) { return; }

        _stream.Close();
        _stream.Dispose();
    }
}
