// Copyright (c) Microsoft. All rights reserved.

using KernelMemory.Main.CLI;

namespace KernelMemory.Main.Tests.Integration;

/// <summary>
/// Real end-to-end test: Config file must exist after ANY write, even the millionth.
/// This tests the actual CLI behavior, not just ConfigParser in isolation.
/// </summary>
public sealed class RealConfigAutoCreationTest : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;


    public RealConfigAutoCreationTest()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"km-real-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }


    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch (IOException)
        {
            // Best effort - ignore IO errors during test cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort - ignore permission errors during test cleanup
        }
    }


    [Fact]
    public void AfterAnyWrite_ConfigFileMustExist()
    {
        // Test the ACTUAL requirement:
        // After running km with a config path, the config file MUST exist

        // Use the actual CLI application builder (how the real app works)
        var builder = new CliApplicationBuilder();

        // Scenario 1: First write
        {
            Assert.False(File.Exists(_configPath), "Config should not exist before first write");

            var app = builder.Build(new[] { "put", "First", "--config", _configPath });
            var exitCode = app.RunAsync(new[] { "put", "First", "--config", _configPath }).GetAwaiter().GetResult();

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(_configPath), "Config MUST exist after first write");
        }

        // Scenario 2: Config deleted, second write
        {
            File.Delete(_configPath);
            Assert.False(File.Exists(_configPath), "Config deleted");

            var app = builder.Build(new[] { "put", "Second", "--config", _configPath });
            var exitCode = app.RunAsync(new[] { "put", "Second", "--config", _configPath }).GetAwaiter().GetResult();

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(_configPath), "Config MUST be recreated after second write");
        }

        // Scenario 3: Config deleted after 10 writes
        {
            for (int i = 0; i < 10; i++)
            {
                if (i == 5)
                {
                    File.Delete(_configPath);
                    Assert.False(File.Exists(_configPath), "Config deleted at iteration 5");
                }

                var app = builder.Build(new[] { "put", $"Record {i}", "--config", _configPath });
                var exitCode = app.RunAsync(new[] { "put", $"Record {i}", "--config", _configPath }).GetAwaiter().GetResult();

                Assert.Equal(0, exitCode);
                Assert.True(File.Exists(_configPath), $"Config MUST exist after write {i}");
            }
        }
    }
}
