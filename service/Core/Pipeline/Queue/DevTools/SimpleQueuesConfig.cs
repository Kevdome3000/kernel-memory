// Copyright (c) Microsoft.All rights reserved.

using System;
using Microsoft.KernelMemory.FileSystem.DevTools;

namespace Microsoft.KernelMemory.Pipeline.Queue.DevTools;

public class SimpleQueuesConfig
{
    public static SimpleQueuesConfig Volatile => new() { StorageType = FileSystemTypes.Volatile };

    public static SimpleQueuesConfig Persistent => new() { StorageType = FileSystemTypes.Disk };

    /// <summary>
    /// The type of storage to use. Defaults to volatile (in RAM).
    /// </summary>
    public FileSystemTypes StorageType { get; set; } = FileSystemTypes.Volatile;

    /// <summary>
    /// Messages storage directory
    /// </summary>
    public string Directory { get; set; } = "tmp-memory-queues";

    /// <summary>
    /// How often to check if there are new messages.
    /// </summary>
    public int PollDelayMsecs { get; set; } = 100;

    /// <summary>
    /// How often to dispatch messages in the queue.
    /// </summary>
    public int DispatchFrequencyMsecs { get; set; } = 100;

    /// <summary>
    /// How many messages to fetch at a time.
    /// </summary>
    public int FetchBatchSize { get; set; } = 3;

    /// <summary>
    /// How long to lock messages once fetched.
    /// </summary>
    public int FetchLockSeconds { get; set; } = 300;

    /// <summary>
    /// How many times to retry processing a failing message.
    /// Example: a value of 20 means that a message will be processed up to 21 times.
    /// </summary>
    public int MaxRetriesBeforePoisonQueue { get; set; } = 1;

    /// <summary>
    /// Suffix used for the poison queue directories
    /// </summary>
    public string PoisonQueueSuffix { get; set; } = "-poison";


    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Directory) || Directory.Contains(' ', StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(Directory)} cannot be empty or have leading or trailing spaces");
        }

        if (string.IsNullOrWhiteSpace(PoisonQueueSuffix) || PoisonQueueSuffix.Contains(' ', StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(PoisonQueueSuffix)} cannot be empty or have leading or trailing spaces");
        }

        if (PollDelayMsecs < 1)
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(PollDelayMsecs)} value {PollDelayMsecs} is too low, cannot be less than 1");
        }

        if (DispatchFrequencyMsecs < 1)
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(DispatchFrequencyMsecs)} value {DispatchFrequencyMsecs} is too low, cannot be less than 1");
        }

        if (FetchBatchSize < 1)
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(FetchBatchSize)} value {FetchBatchSize} is too low, cannot be less than 1");
        }

        if (FetchLockSeconds < 1)
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(FetchLockSeconds)} value {FetchLockSeconds} is too low, cannot be less than 1");
        }

        if (MaxRetriesBeforePoisonQueue < 0)
        {
            throw new ConfigurationException($"SimpleQueue: {nameof(MaxRetriesBeforePoisonQueue)} value {MaxRetriesBeforePoisonQueue} is too low, cannot be less than 0");
        }
    }
}
