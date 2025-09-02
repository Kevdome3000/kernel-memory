// Copyright (c) Microsoft.All rights reserved.

using System;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;

namespace Microsoft.KernelMemory.AI.OpenAI.Internals;

internal sealed class ClientSequentialRetryPolicy : ClientRetryPolicy
{
    private static readonly TimeSpan[] s_retryDelaySequence =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(6),
        TimeSpan.FromSeconds(8)
    ];

    private static readonly TimeSpan s_maxDelay = s_retryDelaySequence[^1];

    private readonly ILogger<ClientSequentialRetryPolicy> _log;


    public ClientSequentialRetryPolicy(
        int maxRetries = 3,
        ILoggerFactory? loggerFactory = null) : base(maxRetries)
    {
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<ClientSequentialRetryPolicy>();
    }


    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        // Check if the remote service specified how long to wait before retrying
        if (TryGetDelayFromResponse(message.Response, out TimeSpan delay))
        {
            _log.LogWarning("Delay extracted from HTTP response: {0} msecs", delay.TotalMilliseconds);
            return delay;
        }

        // Use predefined delay, increasing on each attempt up to a max value
        int index = Math.Max(0, tryCount - 1);
        return index >= s_retryDelaySequence.Length
            ? s_maxDelay
            : s_retryDelaySequence[index];
    }


    private bool TryGetDelayFromResponse(PipelineResponse? response, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;

        if (response == null || response.Status != 429 && response.Status != 503) { return false; }

        delay = TryGetTimeSpanFromHeader(response, "retry-after-ms")
            ?? TryGetTimeSpanFromHeader(response, "x-ms-retry-after-ms")
            ?? TryGetTimeSpanFromHeader(response,
                "Retry-After",
                1000,
                true)
            ?? TimeSpan.Zero;

        return delay > TimeSpan.Zero;
    }


    private TimeSpan? TryGetTimeSpanFromHeader(
        PipelineResponse response,
        string headerName,
        int msecsMultiplier = 1,
        bool allowDateTimeOffset = false)
    {
        if (double.TryParse(
            response.Headers.TryGetValue(headerName, out string? strValue)
                ? strValue
                : null,
            out double doubleValue))
        {
            _log.LogWarning("Header {0} found, value {1}", headerName, doubleValue);
            return TimeSpan.FromMilliseconds(msecsMultiplier * doubleValue);
        }

        if (allowDateTimeOffset && DateTimeOffset.TryParse(headerName, out DateTimeOffset delayUntil))
        {
            _log.LogWarning("Header {0} found, value {1}", headerName, delayUntil);
            return delayUntil - DateTimeOffset.UtcNow;
        }

        return null;
    }
}
