// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory.Text;

namespace Microsoft.KernelMemory.DataFormats.Image;

[Experimental("KMEXP00")]
public sealed class ImageDecoder : IContentDecoder
{
    private readonly IOcrEngine? _ocrEngine;
    private readonly ILogger<ImageDecoder> _log;


    public ImageDecoder(IOcrEngine? ocrEngine = null, ILoggerFactory? loggerFactory = null)
    {
        _ocrEngine = ocrEngine;
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<ImageDecoder>();
    }


    /// <inheritdoc />
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null
            && (
                mimeType.StartsWith(MimeTypes.ImageJpeg, StringComparison.OrdinalIgnoreCase) || mimeType.StartsWith(MimeTypes.ImagePng, StringComparison.OrdinalIgnoreCase) || mimeType.StartsWith(MimeTypes.ImageTiff, StringComparison.OrdinalIgnoreCase)
            );
    }


    /// <inheritdoc />
    public async Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        _log.LogDebug("Extracting text from image file '{0}'", filename);

        var result = new FileContent(MimeTypes.PlainText);
        var content = await ImageToTextAsync(filename, cancellationToken).ConfigureAwait(false);
        result.Sections.Add(new Chunk(content.Trim(), 1, Chunk.Meta(true)));

        return result;
    }


    /// <inheritdoc />
    public async Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        _log.LogDebug("Extracting text from image file");

        var result = new FileContent(MimeTypes.PlainText);
        var content = await ImageToTextAsync(data, cancellationToken).ConfigureAwait(false);
        result.Sections.Add(new Chunk(content.Trim(), 1, Chunk.Meta(true)));

        return result;
    }


    /// <inheritdoc />
    public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        _log.LogDebug("Extracting text from image file");

        var result = new FileContent(MimeTypes.PlainText);
        var content = await ImageToTextAsync(data, cancellationToken).ConfigureAwait(false);
        result.Sections.Add(new Chunk(content, 1, Chunk.Meta(true)));

        return result;
    }


    private async Task<string> ImageToTextAsync(string filename, CancellationToken cancellationToken = default)
    {
        var content = File.OpenRead(filename);

        await using (content.ConfigureAwait(false))
        {
            return await ImageToTextAsync(content, cancellationToken).ConfigureAwait(false);
        }
    }


    private async Task<string> ImageToTextAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        var content = data.ToStream();

        await using (content.ConfigureAwait(false))
        {
            return await ImageToTextAsync(content, cancellationToken).ConfigureAwait(false);
        }
    }


    private async Task<string> ImageToTextAsync(Stream data, CancellationToken cancellationToken = default)
    {
        if (_ocrEngine is null)
        {
            throw new NotSupportedException("Image extraction not configured");
        }

        string text = await _ocrEngine.ExtractTextFromImageAsync(data, cancellationToken).ConfigureAwait(false);
        return text.NormalizeNewlines(true);
    }
}
