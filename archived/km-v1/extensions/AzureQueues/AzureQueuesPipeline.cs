// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Azure;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.DocumentStorage;
using Microsoft.KernelMemory.Pipeline;
using Microsoft.KernelMemory.Pipeline.Queue;
using Timer = System.Timers.Timer;

namespace Microsoft.KernelMemory.Orchestration.AzureQueues;

[Experimental("KMEXP04")]
public sealed class AzureQueuesPipeline : IQueue
{
    private const string DefaultEndpointSuffix = "core.windows.net";

    private static readonly JsonSerializerOptions s_indentedJsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions s_notIndentedJsonOptions = new() { WriteIndented = false };


    private sealed class MessageEventArgs : EventArgs
    {
        public QueueMessage? Message { get; set; }
    }


    /// <summary>
    /// Event triggered when a message is received
    /// </summary>
    private event AsyncMessageHandler<MessageEventArgs>? Received;

    // Queue client builder, requiring the queue name in input
    private readonly Func<string, QueueClient> _clientBuilder;

    // Queue configuration
    private readonly AzureQueuesConfig _config;

    // Queue client, once connected
    private QueueClient? _queue;

    // Queue client, once connected
    private QueueClient? _poisonQueue;

    // Name of the queue
    private string _queueName = string.Empty;

    // Timer triggering the message dispatch
    private Timer? _dispatchTimer;

    // Application logger
    private readonly ILogger<AzureQueuesPipeline> _log;

    // Lock helpers
    private readonly object _lock = new();
    private bool _busy = false;
    private readonly CancellationTokenSource _cancellation = new();


    public AzureQueuesPipeline(
        AzureQueuesConfig config,
        ILoggerFactory? loggerFactory = null)
    {
        _config = config;
        _config.Validate();

        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<AzureQueuesPipeline>();

        var clientOptions = GetClientOptions(config);

        switch (config.Auth)
        {
            case AzureQueuesConfig.AuthTypes.ConnectionString:
            {
                ValidateConnectionString(config.ConnectionString);
                _clientBuilder = queueName => new QueueClient(config.ConnectionString, queueName, clientOptions);
                break;
            }

            case AzureQueuesConfig.AuthTypes.AccountKey:
            {
                ValidateAccountName(config.Account);
                ValidateAccountKey(config.AccountKey);
                var suffix = ValidateEndpointSuffix(config.EndpointSuffix);
                _clientBuilder = queueName => new QueueClient(
                    new Uri($"https://{config.Account}.queue.{suffix}/{queueName}"),
                    new StorageSharedKeyCredential(config.Account, config.AccountKey),
                    clientOptions);
                break;
            }

            case AzureQueuesConfig.AuthTypes.AzureIdentity:
            {
                ValidateAccountName(config.Account);
                var suffix = ValidateEndpointSuffix(config.EndpointSuffix);
                _clientBuilder = queueName => new QueueClient(
                    new Uri($"https://{config.Account}.queue.{suffix}/{queueName}"),
                    new DefaultAzureCredential(),
                    clientOptions);
                break;
            }

            case AzureQueuesConfig.AuthTypes.ManualStorageSharedKeyCredential:
            {
                ValidateAccountName(config.Account);
                var suffix = ValidateEndpointSuffix(config.EndpointSuffix);
                _clientBuilder = queueName => new QueueClient(new Uri($"https://{config.Account}.queue.{suffix}/{queueName}"),
                    config.GetStorageSharedKeyCredential(),
                    clientOptions);
                break;
            }

            case AzureQueuesConfig.AuthTypes.ManualAzureSasCredential:
            {
                ValidateAccountName(config.Account);
                var suffix = ValidateEndpointSuffix(config.EndpointSuffix);
                _clientBuilder = queueName => new QueueClient(new Uri($"https://{config.Account}.queue.{suffix}/{queueName}"),
                    config.GetAzureSasCredential(),
                    clientOptions);
                break;
            }

            case AzureQueuesConfig.AuthTypes.ManualTokenCredential:
            {
                ValidateAccountName(config.Account);
                var suffix = ValidateEndpointSuffix(config.EndpointSuffix);
                _clientBuilder = queueName => new QueueClient(new Uri($"https://{config.Account}.queue.{suffix}/{queueName}"),
                    config.GetTokenCredential(),
                    clientOptions);
                break;
            }

            default:
            case AzureQueuesConfig.AuthTypes.Unknown:
                _log.LogCritical("Azure Queue authentication type '{0}' undefined or not supported", config.Auth);
                throw new DocumentStorageException($"Azure Queue authentication type '{config.Auth}' undefined or not supported");
        }
    }


    /// <inheritdoc />
    public async Task<IQueue> ConnectToQueueAsync(string queueName, QueueOptions options = default, CancellationToken cancellationToken = default)
    {
        queueName = CleanQueueName(queueName);
        _log.LogTrace("Connecting to queue name: {0}", queueName);

        ArgumentNullExceptionEx.ThrowIfNullOrWhiteSpace(queueName, nameof(queueName), "The queue name is empty");

        if (!string.IsNullOrEmpty(_queueName))
        {
            _log.LogError("The queue name has already been set, already connected to {0}", _queueName);
            throw new InvalidOperationException($"The queue is already connected to `{_queueName}`");
        }

        // Note: 3..63 chars, only lowercase letters, numbers and hyphens. No hyphens at start/end and no consecutive hyphens.
        _queueName = queueName;
        _log.LogDebug("Queue name: {0}", _queueName);

        _queue = _clientBuilder(_queueName);
        Response? result = await _queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _log.LogTrace("Queue ready: status code {0}", result?.Status);

        _poisonQueue = _clientBuilder(_queueName + _config.PoisonQueueSuffix);
        result = await _poisonQueue.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _log.LogTrace("Poison queue ready: status code {0}", result?.Status);

        if (options.DequeueEnabled)
        {
            _log.LogTrace("Enabling dequeue on queue {0}, every {1} msecs", _queueName, _config.PollDelayMsecs);
            _dispatchTimer = new Timer(_config.PollDelayMsecs); // milliseconds
            _dispatchTimer.Elapsed += DispatchMessages;
            _dispatchTimer.Start();
        }

        return this;
    }


    /// <inheritdoc />
    public async Task EnqueueAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_queueName) || _queue == null)
        {
            _log.LogError("The queue client is not connected, cannot enqueue messages");
            throw new InvalidOperationException("The client must be connected to a queue first");
        }

        _log.LogDebug("Sending message...");
        Response<SendReceipt> receipt = await _queue.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        _log.LogDebug("Message sent {0}", receipt.Value?.MessageId);
    }


    /// <inheritdoc />
    public void OnDequeue(Func<string, Task<ReturnType>> processMessageAction)
    {
        Received += async (sender, args) =>
        {
            ArgumentNullExceptionEx.ThrowIfNull(args.Message, nameof(args.Message), "The message received is NULL");
            QueueMessage message = args.Message;

            _log.LogInformation("Message '{0}' received, expires at {1}", message.MessageId, message.ExpiresOn);

            try
            {
                ReturnType returnType = await processMessageAction.Invoke(message.MessageText).ConfigureAwait(false);

                if (message.DequeueCount <= _config.MaxRetriesBeforePoisonQueue)
                {
                    switch (returnType)
                    {
                        case ReturnType.Success:
                            _log.LogTrace("Message '{0}' successfully processed, deleting message", message.MessageId);
                            await DeleteMessageAsync(message, default).ConfigureAwait(false);
                            break;

                        case ReturnType.TransientError:
                            var backoffDelay = TimeSpan.FromSeconds(1 * message.DequeueCount);
                            _log.LogWarning("Message '{0}' failed to process, putting message back in the queue with a delay of {1} msecs",
                                message.MessageId,
                                backoffDelay.TotalMilliseconds);
                            await UnlockMessageAsync(message, backoffDelay, default).ConfigureAwait(false);
                            break;

                        case ReturnType.FatalError:
                            _log.LogError("Message '{0}' failed to process due to a non-recoverable error, moving to poison queue", message.MessageId);
                            await MoveMessageToPoisonQueueAsync(message, default).ConfigureAwait(false);
                            break;

                        default:
                            throw new ArgumentOutOfRangeException($"Unknown {returnType:G} result");
                    }
                }
                else
                {
                    _log.LogError("Message '{0}' reached max attempts, moving to poison queue", message.MessageId);
                    await MoveMessageToPoisonQueueAsync(message, default).ConfigureAwait(false);
                }
            }
            catch (KernelMemoryException e) when (e.IsTransient.HasValue && !e.IsTransient.Value)
            {
                _log.LogError(e, "Message '{0}' failed to process due to a non-recoverable error, moving to poison queue", message.MessageId);
                await MoveMessageToPoisonQueueAsync(message, default).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Must catch all to handle queue properly
            catch (Exception e)
            {
                // Exceptions caught by this block:
                // - message processing failed with exception
                // - failed to delete message from queue
                // - failed to unlock message in the queue
                // - failed to move message to poison queue

                var backoffDelay = TimeSpan.FromSeconds(1 * message.DequeueCount);
                _log.LogWarning(e,
                    "Message '{0}' processing failed with exception, putting message back in the queue with a delay of {1} msecs",
                    message.MessageId,
                    backoffDelay.TotalMilliseconds);

                // Note: if this fails, the exception is caught by this.DispatchMessages()
                await UnlockMessageAsync(message, backoffDelay, default).ConfigureAwait(false);
            }
#pragma warning restore CA1031
        };
    }


    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
        _dispatchTimer?.Dispose();
    }


    /// <summary>
    /// Options used by the Azure Queue client, e.g. User Agent and Auth tokens audience, etc.
    /// </summary>
    private static QueueClientOptions GetClientOptions(AzureQueuesConfig config)
    {
        var options = new QueueClientOptions
        {
            Diagnostics =
            {
                IsTelemetryEnabled = Telemetry.IsTelemetryEnabled,
                ApplicationId = Telemetry.HttpUserAgent
            }
        };

        if (config.Auth == AzureQueuesConfig.AuthTypes.AzureIdentity && !string.IsNullOrWhiteSpace(config.AzureIdentityAudience))
        {
            options.Audience = new QueueAudience(config.AzureIdentityAudience);
        }

        return options;
    }


    /// <summary>
    /// Fetch messages from the queue and dispatch them
    /// </summary>
    private void DispatchMessages(object? sender, ElapsedEventArgs ev)
    {
        if (_busy || Received == null || _queue == null)
        {
            return;
        }

        lock (_lock)
        {
            _busy = true;

            QueueMessage[] messages = [];

            // Fetch messages
            try
            {
                // Fetch and Hide N messages
                Response<QueueMessage[]> receiveMessages = _queue.ReceiveMessages(_config.FetchBatchSize, TimeSpan.FromSeconds(_config.FetchLockSeconds));

                if (receiveMessages.HasValue && receiveMessages.Value.Length > 0)
                {
                    messages = receiveMessages.Value;
                }
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Fetch failed");
                _busy = false;
                throw;
            }

            if (messages.Length == 0)
            {
                _busy = false;
                return;
            }

            // Async messages dispatch
            _log.LogTrace("Dispatching {0} messages", messages.Length);

            foreach (QueueMessage message in messages)
            {
                _ = Task.Factory.StartNew(
                    async _ =>
                    {
                        try
                        {
                            _log.LogTrace("Message content: {0}", message.MessageText);
                            await Received(this, new MessageEventArgs { Message = message }).ConfigureAwait(false);
                        }
#pragma warning disable CA1031 // Must catch all to log and keep the process alive
                        catch (Exception e)
                        {
                            _log.LogError(e, "Message '{0}' processing failed with exception", message.MessageId);
                        }
#pragma warning restore CA1031
                    },
                    null,
                    _cancellation.Token,
                    TaskCreationOptions.RunContinuationsAsynchronously,
                    TaskScheduler.Current
                );
            }

            _busy = false;
        }
    }


    private async Task DeleteMessageAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        await _queue!.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken).ConfigureAwait(false);
    }


    private async Task UnlockMessageAsync(QueueMessage message, TimeSpan delay, CancellationToken cancellationToken)
    {
        await _queue!.UpdateMessageAsync(message.MessageId,
                message.PopReceipt,
                visibilityTimeout: delay,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }


    private async Task MoveMessageToPoisonQueueAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        await _poisonQueue!.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var poisonMsg = new
        {
            message.MessageText,
            Id = message.MessageId,
            message.InsertedOn,
            message.DequeueCount
        };

        var neverExpire = TimeSpan.FromSeconds(-1);
        await _poisonQueue.SendMessageAsync(
                ToJson(poisonMsg),
                TimeSpan.Zero,
                neverExpire,
                cancellationToken)
            .ConfigureAwait(false);
        await DeleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }


    private void ValidateAccountName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _log.LogCritical("The Azure Queue account name is empty");
            throw new DocumentStorageException("The account name is empty");
        }
    }


    private void ValidateAccountKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _log.LogCritical("The Azure Queue account key is empty");
            throw new DocumentStorageException("The Azure Queue account key is empty");
        }
    }


    private void ValidateConnectionString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _log.LogCritical("The Azure Queue connection string is empty");
            throw new DocumentStorageException("The Azure Queue connection string is empty");
        }
    }


    private string ValidateEndpointSuffix(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            value = DefaultEndpointSuffix;
            _log.LogError("The Azure Queue account endpoint suffix is empty, using default value {0}", value);
        }

        return value;
    }


    private static string ToJson(object data, bool indented = false)
    {
        return JsonSerializer.Serialize(data,
            indented
                ? s_indentedJsonOptions
                : s_notIndentedJsonOptions);
    }


    private static string CleanQueueName(string? name)
    {
        return name?.ToLowerInvariant().Replace('_', '-').Replace(' ', '-').Replace('.', '-') ?? string.Empty;
    }
}
