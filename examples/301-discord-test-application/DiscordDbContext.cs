// Copyright (c) Microsoft. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace _301_discord_test_application;

public class DiscordDbContext : DbContext
{
    public DbContextOptions<DiscordDbContext> Options { get; }

    // Table to store Discord messages, table name is "Messages"
    public DbSet<DiscordDbMessage> Messages { get; set; }


    public DiscordDbContext(DbContextOptions<DiscordDbContext> options) : base(options)
    {
        Options = options;
    }
}
