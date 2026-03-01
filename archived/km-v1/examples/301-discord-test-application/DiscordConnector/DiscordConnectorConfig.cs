// Copyright (c) Microsoft. All rights reserved.

namespace _301_discord_test_application.DiscordConnector;

/// <summary>
/// Discord bot settings
/// </summary>
public class DiscordConnectorConfig
{
    /// <summary>
    /// Discord bot authentication token
    /// </summary>
    public string DiscordToken { get; set; } = string.Empty;

    /// <summary>
    /// Index where to store files (not memories)
    /// </summary>
    public string Index { get; set; } = "discord";

    /// <summary>
    /// File name used when uploading a message.
    /// </summary>
    public string FileName { get; set; } = "discord.json";

    /// <summary>
    /// Handlers processing the incoming Discord events
    /// </summary>
    public List<string> Steps { get; set; } = [];
}
