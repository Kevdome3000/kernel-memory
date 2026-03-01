// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;
using _301_discord_test_application.DiscordConnector;

namespace _301_discord_test_application;

public class DiscordDbMessage : DiscordMessage
{
    [Key]
    public string Id
    {
        get => MessageId;
        set => MessageId = value;
    }
}
