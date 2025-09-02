// Copyright (c) Microsoft. All rights reserved.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Diagnostics;

namespace _301_discord_test_application.DiscordConnector;

/// <summary>
/// Service responsible for connecting to Discord, listening for messages
/// and generating events for Kernel Memory.
/// </summary>
public sealed class DiscordConnector : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly IKernelMemory _memory;
    private readonly ILogger<DiscordConnector> _log;
    private readonly string _authToken;
    private readonly string _docStorageIndex;
    private readonly string _docStorageFilename;
    private readonly List<string> _pipelineSteps;


    /// <summary>
    /// New instance of Discord bot
    /// </summary>
    /// <param name="config">Discord settings</param>
    /// <param name="memory">Memory instance used to upload files when messages arrives</param>
    /// <param name="loggerFactory">App log factory</param>
    public DiscordConnector(
        DiscordConnectorConfig config,
        IKernelMemory memory,
        ILoggerFactory? loggerFactory = null)
    {
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<DiscordConnector>();
        _authToken = config.DiscordToken;

        var dc = new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Debug,
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
            LogGatewayIntentWarnings = true,
            SuppressUnknownDispatchWarnings = false
        };

        _client = new DiscordSocketClient(dc);
        _client.Log += OnLog;
        _client.MessageReceived += OnMessage;
        _memory = memory;
        _docStorageIndex = config.Index;
        _pipelineSteps = config.Steps;
        _docStorageFilename = config.FileName;
    }


    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _client.LoginAsync(TokenType.Bot, _authToken).ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.LogoutAsync().ConfigureAwait(false);
        await _client.StopAsync().ConfigureAwait(false);
    }


    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }


    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }


    #region private

    private static readonly Dictionary<LogSeverity, LogLevel> s_logLevels = new()
    {
        [LogSeverity.Critical] = LogLevel.Critical,
        [LogSeverity.Error] = LogLevel.Error,
        [LogSeverity.Warning] = LogLevel.Warning,
        [LogSeverity.Info] = LogLevel.Information,
        [LogSeverity.Verbose] = LogLevel.Debug, // note the inconsistency
        [LogSeverity.Debug] = LogLevel.Trace // note the inconsistency
    };


    private Task OnMessage(SocketMessage message)
    {
        var msg = new DiscordMessage
        {
            MessageId = message.Id.ToString(CultureInfo.InvariantCulture),
            AuthorId = message.Author.Id.ToString(CultureInfo.InvariantCulture),
            ChannelId = message.Channel.Id.ToString(CultureInfo.InvariantCulture),
            ReferenceMessageId = message.Reference?.MessageId.ToString() ?? string.Empty,
            AuthorUsername = message.Author.Username,
            ChannelName = message.Channel.Name,
            Timestamp = message.Timestamp,
            Content = message.Content,
            CleanContent = message.CleanContent,
            EmbedsCount = message.Embeds.Count
        };

        if (message.Channel is SocketTextChannel textChannel)
        {
            msg.ChannelMention = textChannel.Mention;
            msg.ChannelTopic = textChannel.Topic;
            msg.ServerId = textChannel.Guild.Id.ToString(CultureInfo.InvariantCulture);
            msg.ServerName = textChannel.Guild.Name;
            msg.ServerDescription = textChannel.Guild.Description;
            msg.ServerMemberCount = textChannel.Guild.MemberCount;
        }

        _log.LogTrace("[{0}] New message from '{1}' [{2}]",
            msg.MessageId,
            msg.AuthorUsername,
            msg.AuthorId);
        _log.LogTrace("[{0}] Channel: {1}", msg.MessageId, msg.ChannelId);
        _log.LogTrace("[{0}] Channel: {1}", msg.MessageId, msg.ChannelName);
        _log.LogTrace("[{0}] Timestamp: {1}", msg.MessageId, msg.Timestamp);
        _log.LogTrace("[{0}] Content: {1}", msg.MessageId, msg.Content);
        _log.LogTrace("[{0}] CleanContent: {1}", msg.MessageId, msg.CleanContent);
        _log.LogTrace("[{0}] Reference: {1}", msg.MessageId, msg.ReferenceMessageId);
        _log.LogTrace("[{0}] EmbedsCount: {1}", msg.MessageId, msg.EmbedsCount);

        if (message.Embeds.Count > 0)
        {
            foreach (Embed? x in message.Embeds)
            {
                if (x == null) { continue; }

                _log.LogTrace("[{0}] Embed Title: {1}", message.Id, x.Title);
                _log.LogTrace("[{0}] Embed Url: {1}", message.Id, x.Url);
                _log.LogTrace("[{0}] Embed Description: {1}", message.Id, x.Description);
            }
        }

        Task.Run(async () =>
        {
            string documentId = $"{msg.ServerId}_{msg.ChannelId}_{msg.MessageId}";
            string content = JsonSerializer.Serialize(msg);
            Stream fileContent = new MemoryStream(Encoding.UTF8.GetBytes(content), false);

            await using (fileContent.ConfigureAwait(false))
            {
                await _memory.ImportDocumentAsync(
                        fileContent,
                        _docStorageFilename,
                        documentId,
                        index: _docStorageIndex,
                        steps: _pipelineSteps)
                    .ConfigureAwait(false);
            }
        });

        return Task.CompletedTask;
    }


    private Task OnLog(LogMessage msg)
    {
        var logLevel = LogLevel.Information;

        if (s_logLevels.TryGetValue(msg.Severity, out LogLevel value))
        {
            logLevel = value;
        }

        _log.Log(logLevel,
            "{0}: {1}",
            msg.Source,
            msg.Message);

        return Task.CompletedTask;
    }

    #endregion


}
