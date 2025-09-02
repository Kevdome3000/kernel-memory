// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Timer = System.Timers.Timer;

namespace Microsoft.KernelMemory.Pipeline.Queue.DevTools;

/// <summary>
/// Basic implementation of a file based queue for local testing.
/// This is not meant for production scenarios, only to avoid spinning up additional services.
/// </summary>
[Experimental("KMEXP04")]
#pragma warning disable CA1031 // need to log all errors
public sealed class SimpleQueues : IQueue
{
    private readonly SimpleQueuesConfig _config;


    private sealed class MessageEventArgs : EventArgs
    {
        public Message? Message { get; set; }
    }


    /// <summary>
    /// Event triggered when a message is received
    /// TODO: move to async events
    /// </summary>
    private event EventHandler<MessageEventArgs>? Received;

    // Extension of the files containing the messages. Don't leave this empty, it's better
    // filtering and it mitigates the risk of unwanted file deletions.
    private const string FileExt = ".sqm.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    // Lock helpers. This is static so that processes sharing the same storage don't conflict with each other.
    private static readonly SemaphoreSlim s_lock = new(1, 1);

    // Underlying storage where messages and queues are stored
    private readonly IFileSystem _fileSystem;

    // Application logger
    private readonly ILogger<SimpleQueues> _log;

    private readonly ConcurrentQueue<Message> _queue = new();

    // Max attempts at processing each message
    private readonly int _maxAttempts;

    private readonly CancellationTokenSource _cancellation;

    // Name of the queue, used also as a directory name
    private string _queueName = string.Empty;

    // Name of the poison queue, used also as a directory name
    private string _poisonQueueName = string.Empty;

    // Timer triggering the filesystem read
    private Timer? _populateTimer;

    // Timer triggering the message dispatch
    private Timer? _dispatchTimer;


    /// <summary>
    /// Create new file based queue
    /// </summary>
    /// <param name="config">File queue configuration</param>
    /// <param name="loggerFactory">Application logger factory</param>
    /// <exception cref="InvalidOperationException"></exception>
    public SimpleQueues(SimpleQueuesConfig config, ILoggerFactory? loggerFactory = null)
    {
        config.Validate();
        _config = config;

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<SimpleQueues>();
        _cancellation = new CancellationTokenSource();

        switch (config.StorageType)
        {
            case FileSystemTypes.Disk:
                _log.LogTrace("Using {StorageType} storage", nameof(DiskFileSystem));
                _fileSystem = new DiskFileSystem(config.Directory, null, loggerFactory);
                break;

            case FileSystemTypes.Volatile:
                _log.LogTrace("Using {StorageType} storage", nameof(VolatileFileSystem));
                _fileSystem = VolatileFileSystem.GetInstance(config.Directory, null, loggerFactory);
                break;

            default:
                _log.LogCritical("Unknown storage type {StorageType}", config.StorageType);
                throw new ArgumentException($"Unknown storage type {config.StorageType}");
        }

        _maxAttempts = config.MaxRetriesBeforePoisonQueue + 1;
    }


    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation?.Cancel();
        _populateTimer?.Dispose();
        _dispatchTimer?.Dispose();
        _cancellation?.Dispose();
    }


    /// <inheritdoc />
    public async Task<IQueue> ConnectToQueueAsync(string queueName, QueueOptions options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(queueName, nameof(queueName), "The queue name is empty");

        if (queueName == _queueName) { return this; }

        if (!string.IsNullOrEmpty(_queueName))
        {
            _log.LogCritical("The client is already connected to queue {QueueName}", _queueName);
            throw new InvalidOperationException($"The queue is already connected to `{_queueName}`");
        }

        _queueName = queueName;
        _poisonQueueName = $"{queueName}{_config.PoisonQueueSuffix}";
        await CreateDirectoriesAsync(cancellationToken).ConfigureAwait(false);

        _log.LogTrace("Client connected to queue {QueueName} and poison queue {PoisonQueueName}", _queueName, _poisonQueueName);

        if (options.DequeueEnabled)
        {
            _populateTimer = new Timer(_config.PollDelayMsecs);
            _populateTimer.Elapsed += PopulateQueue;
            _populateTimer.Start();

            _dispatchTimer = new Timer(_config.DispatchFrequencyMsecs);
            _dispatchTimer.Elapsed += DispatchMessage;
            _dispatchTimer.Start();

            _log.LogTrace("Queue {QueueName}: polling and dispatching timers created", _queueName);
        }
        else
        {
            _log.LogTrace("Queue {QueueName}: dequeue not enabled", _queueName);
        }

        return this;
    }


    /// <inheritdoc />
    public async Task EnqueueAsync(string message, CancellationToken cancellationToken = default)
    {
        // Use a sortable file name. Don't use UTC for local development.
        var messageId = DateTimeOffset.Now.ToString("yyyyMMdd.HHmmss.fffffff", CultureInfo.InvariantCulture)
            + "."
            + Guid.NewGuid().ToString("N");

        await StoreMessageAsync(
                _queueName,
                new Message
                {
                    Id = messageId,
                    Content = message,
                    DequeueCount = 0,
                    Schedule = DateTimeOffset.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);

        _log.LogInformation("Queue {QueueName}: message {MessageId} sent", _queueName, messageId);
    }


    /// <inheritdoc />
    /// <see cref="DistributedPipelineOrchestrator.AddHandlerAsync"/> about the logic handling dequeued messages.
    public void OnDequeue(Func<string, Task<ReturnType>> processMessageAction)
    {
        _log.LogInformation("Queue {QueueName}: subscribing...", _queueName);
        Received += async (sender, args) =>
        {
            Message message = new();
            var retry = false;
            var poison = false;

            try
            {
                ArgumentNullExceptionEx.ThrowIfNull(args.Message, nameof(args.Message), "The message received is NULL");
                message = args.Message;

                _log.LogInformation("Queue {QueueName}: message {MessageId} received", _queueName, message.Id);

                // Process message with the logic provided by the orchestrator
                var returnType = await processMessageAction.Invoke(message.Content).ConfigureAwait(false);

                switch (returnType)
                {
                    case ReturnType.Success:
                        _log.LogTrace("Message '{MessageId}' successfully processed, deleting message", message.Id);
                        await DeleteMessageAsync(message.Id, _cancellation.Token).ConfigureAwait(false);
                        break;

                    case ReturnType.TransientError:
                        message.LastError = "Message handler returned false";

                        if (message.DequeueCount == _maxAttempts)
                        {
                            _log.LogError("Message '{MessageId}' processing failed to process, max attempts reached, moving to poison queue. Message content: {MessageContent}", message.Id, message.Content);
                            poison = true;
                        }
                        else
                        {
                            _log.LogWarning("Message '{MessageId}' failed to process, putting message back in the queue. Message content: {MessageContent}", message.Id, message.Content);
                            retry = true;
                        }

                        break;

                    case ReturnType.FatalError:
                        _log.LogError("Message '{MessageId}' failed to process due to a non-recoverable error, moving to poison queue", message.Id);
                        poison = true;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException($"Unknown {returnType:G} result");
                }
            }
            catch (KernelMemoryException e) when (e.IsTransient.HasValue && !e.IsTransient.Value)
            {
                message.LastError = $"{e.GetType().FullName} [{e.InnerException?.GetType().FullName}]: {e.Message}";
                _log.LogError(e, "Message '{MessageId}' failed to process due to a non-recoverable error, moving to poison queue.", message.Id);
                poison = true;
            }
            // Note: must catch all also because using a void event handler
            catch (Exception e)
            {
                message.LastError = $"{e.GetType().FullName}: {e.Message}";

                if (message.DequeueCount == _maxAttempts)
                {
                    _log.LogError(e,
                        "Message '{MessageId}' processing failed with exception, max attempts reached, moving to poison queue. Message content: {MessageContent}.",
                        message.Id,
                        message.Content);
                    poison = true;
                }
                else
                {
                    _log.LogWarning(e,
                        "Message '{MessageId}' processing failed with exception, putting message back in the queue. Message content: {MessageContent}.",
                        message.Id,
                        message.Content);
                    retry = true;
                }
            }

            message.Unlock();

            if (retry)
            {
                var backoffDelay = TimeSpan.FromSeconds(1 * message.DequeueCount);
                message.RunIn(backoffDelay);
                await StoreMessageAsync(_queueName, message, _cancellation.Token).ConfigureAwait(false);
            }
            else if (poison)
            {
                await StoreMessageAsync(_poisonQueueName, message, _cancellation.Token).ConfigureAwait(false);
                await DeleteMessageAsync(message.Id, _cancellation.Token).ConfigureAwait(false);
            }
        };
    }


    /// <summary>
    /// Read messages from the file system and store the in memory, ready to be dispatched.
    /// Use a lock to avoid unnecessary file system reads.
    /// </summary>
    private void PopulateQueue(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        Task.Run(async () =>
            {
                var lockAcquired = false;

                try
                {
                    if (_queue.Count >= _config.FetchBatchSize) { return; }

                    await s_lock.WaitAsync(_cancellation.Token).ConfigureAwait(false);
                    lockAcquired = true;

                    // Loop through all messages on storage
                    var messagesOnStorage = (await _fileSystem.GetAllFileNamesAsync(_queueName, "", _cancellation.Token).ConfigureAwait(false)).ToList();

                    if (messagesOnStorage.Count == 0) { return; }

                    _log.LogTrace("Queue {QueueName}: {MsgCountOnStorage} messages on storage, {MsgCountReady} ready to dispatch, max batch size {FetchBatchSize}",
                        _queueName,
                        messagesOnStorage.Count,
                        _queue.Count,
                        _config.FetchBatchSize);

                    foreach (var fileName in messagesOnStorage)
                    {
                        // Limit the number of messages loaded in memory
                        if (_queue.Count >= _config.FetchBatchSize)
                        {
                            _log.LogTrace("Queue {QueueName}: max batch size {FetchBatchSize} reached", _queueName, _config.FetchBatchSize);
                            return;
                        }

                        // Ignore files that are not messages
                        if (!fileName.EndsWith(FileExt, StringComparison.OrdinalIgnoreCase)) { continue; }

                        // Load message from storage
                        var messageId = fileName.Substring(0, fileName.Length - FileExt.Length);
                        var message = await ReadMessageAsync(messageId, _cancellation.Token).ConfigureAwait(false);

                        // Avoid enqueueing the same message twice, even if not locked, to avoid double execution
                        if (message.IsTimeToRun() && !message.IsLocked() && _queue.All(x => x.Id != messageId))
                        {
                            // Update message metadata
                            message.Lock(_config.FetchLockSeconds);
                            message.DequeueCount++;
                            await StoreMessageAsync(_queueName, message, _cancellation.Token).ConfigureAwait(false);

                            // Add to list of messages to be processed
                            _queue.Enqueue(message);
                            _log.LogTrace("Queue {QueueName}: found message {MessageId}", _queueName, messageId);
                        }

                        if (_log.IsEnabled(LogLevel.Trace))
                        {
                            if (!message.IsTimeToRun())
                            {
                                _log.LogTrace("Queue {QueueName}: skipping message {MessageId} scheduled in the future", _queueName, messageId);
                            }
                            else if (message.IsLocked())
                            {
                                _log.LogTrace("Queue {QueueName}: skipping message {MessageId} because it is locked", _queueName, messageId);
                            }
                            else if (_queue.Any(x => x.Id == messageId))
                            {
                                _log.LogTrace("Queue {QueueName}: skipping message {MessageId} because it is already loaded", _queueName, messageId);
                            }
                        }
                    }
                }
                catch (DirectoryNotFoundException e)
                {
                    _log.LogError(e, "Directory missing, recreating.");
                    await CreateDirectoriesAsync(_cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Queue {QueueName}: Unexpected error while polling.", _queueName);
                }
                finally
                {
                    // Decrease the internal counter only it the lock was acquired,
                    // e.g. not when WaitAsync times out or throws some exception
                    if (lockAcquired)
                    {
                        s_lock.Release();
                    }
                }
            },
            _cancellation.Token);
    }


    /// <summary>
    /// Dispatch messages in memory, previously loaded from file system by <see cref="PopulateQueue"/>.
    /// Use a lock to avoid dispatching the same messages more than once.
    /// <see cref="OnDequeue"/> to track how messages flow externally.
    /// </summary>
    private void DispatchMessage(object? sender, ElapsedEventArgs e)
    {
        Task.Run(async () =>
            {
                var lockAcquired = false;

                try
                {
                    if (_queue.IsEmpty) { return; }

                    await s_lock.WaitAsync(_cancellation.Token).ConfigureAwait(false);
                    lockAcquired = true;

                    _log.LogTrace("Dispatching {MessageCount} messages", _queue.Count);

                    while (_queue.TryDequeue(out Message? message))
                    {
                        Received?.Invoke(this, new MessageEventArgs { Message = message });
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Queue {QueueName}: Unexpected error while dispatching", _queueName);
                }
                finally
                {
                    // Decrease the internal counter only it the lock was acquired,
                    // e.g. not when WaitAsync times out or throws some exception
                    if (lockAcquired)
                    {
                        s_lock.Release();
                    }
                }
            },
            _cancellation.Token);
    }


    private static string Serialize(Message msg) { return JsonSerializer.Serialize(msg, s_jsonOptions); }

    private static Message Deserialize(string json) { return JsonSerializer.Deserialize<Message>(json) ?? new Message(); }


    private async Task<Message> ReadMessageAsync(string id, CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Queue {QueueName}: reading message {MessageId}", _queueName, id);
        var serializedMsg = await _fileSystem.ReadFileAsTextAsync(
                _queueName,
                "",
                $"{id}{FileExt}",
                cancellationToken)
            .ConfigureAwait(false);
        return Deserialize(serializedMsg);
    }


    private async Task StoreMessageAsync(string queueName, Message message, CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Queue {QueueName}: storing message {MessageId}", _queueName, message.Id);
        await _fileSystem.WriteFileAsync(queueName,
                "",
                $"{message.Id}{FileExt}",
                Serialize(message),
                cancellationToken)
            .ConfigureAwait(false);
    }


    private async Task DeleteMessageAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            _log.LogTrace("Queue {QueueName}: deleting message {MessageId}", _queueName, id);
            var fileName = $"{id}{FileExt}";
            _log.LogTrace("Deleting file from storage {FileName}", fileName);
            await _fileSystem.DeleteFileAsync(_queueName,
                    "",
                    fileName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DirectoryNotFoundException)
        {
            await CreateDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Error while deleting message from storage");
        }
    }


    private async Task CreateDirectoriesAsync(CancellationToken cancellationToken = default)
    {
        await _fileSystem.CreateVolumeAsync(_queueName, cancellationToken).ConfigureAwait(false);
        await _fileSystem.CreateVolumeAsync(_poisonQueueName, cancellationToken).ConfigureAwait(false);
    }
}

#pragma warning restore CA1031
