// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Text.Json;

namespace KernelMemory.Main.Tests.Integration;

/// <summary>
/// End-to-end CLI tests using actual process execution.
/// Executes km commands as separate processes and verifies actual JSON output.
/// Tests the COMPLETE path including all CLI layers, formatting, and output.
/// </summary>
public sealed class SearchProcessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _kmPath;


    public SearchProcessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"km-process-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _configPath = Path.Combine(_tempDir, "config.json");

        // Find the km binary (from build output)
        // Get solution root by going up from test assembly location
        var testAssemblyPath = typeof(SearchProcessTests).Assembly.Location;
        var testBinDir = Path.GetDirectoryName(testAssemblyPath)!;
        var solutionRoot = Path.GetFullPath(Path.Combine(testBinDir, "../../../../.."));
        _kmPath = Path.Combine(solutionRoot, "src/Main/bin/Debug/net10.0/KernelMemory.Main.dll");

        if (!File.Exists(_kmPath))
        {
            throw new FileNotFoundException($"KernelMemory.Main.dll not found at {_kmPath}. Run dotnet build first.");
        }
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
            // Ignore cleanup errors
        }
    }


    /// <summary>
    /// Execute km command and return JSON output.
    /// </summary>
    /// <param name="args">Command line arguments to pass to km.</param>
    /// <returns>Standard output from the command.</returns>
    private async Task<string> ExecuteKmAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{_kmPath} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);

        if (process == null)
        {
            throw new InvalidOperationException("Failed to start km process");
        }

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"km command failed (exit {process.ExitCode}): {error}");
        }

        return output.Trim();
    }


    [OllamaFact]
    public async Task Process_PutThenSearch_FindsContent()
    {
        // Act: Insert content
        var putOutput = await ExecuteKmAsync($"put \"ciao mondo\" --config {_configPath}").ConfigureAwait(false);
        var putResult = JsonSerializer.Deserialize<JsonElement>(putOutput);
        var insertedId = putResult.GetProperty("id").GetString();
        Assert.NotNull(insertedId);
        Assert.True(putResult.GetProperty("completed").GetBoolean());

        // Act: Search for content
        var searchOutput = await ExecuteKmAsync($"search \"ciao\" --config {_configPath} --format json").ConfigureAwait(false);
        var searchResult = JsonSerializer.Deserialize<JsonElement>(searchOutput);

        // Assert: Verify actual results
        Assert.Equal(1, searchResult.GetProperty("totalResults").GetInt32());
        var results = searchResult.GetProperty("results").EnumerateArray().ToArray();
        Assert.Single(results);
        Assert.Equal(insertedId, results[0].GetProperty("id").GetString());
        Assert.Contains("ciao", results[0].GetProperty("content").GetString()!, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Process_BooleanAnd_FindsOnlyMatchingBoth()
    {
        // Arrange
        await ExecuteKmAsync($"put \"docker and kubernetes\" --config {_configPath}").ConfigureAwait(false);
        await ExecuteKmAsync($"put \"only docker\" --config {_configPath}").ConfigureAwait(false);

        // Act
        var output = await ExecuteKmAsync($"search \"docker AND kubernetes\" --config {_configPath} --format json").ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(output);

        // Assert
        Assert.Equal(1, result.GetProperty("totalResults").GetInt32());
        var content = result.GetProperty("results")[0].GetProperty("content").GetString()!;
        Assert.Contains("docker", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kubernetes", content, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Process_FieldSpecificStemming_FindsVariations()
    {
        // Arrange
        var putOutput = await ExecuteKmAsync($"put \"summary findings\" --config {_configPath}").ConfigureAwait(false);
        var putResult = JsonSerializer.Deserialize<JsonElement>(putOutput);
        var id = putResult.GetProperty("id").GetString();

        // Act: Search for plural form in content field
        var searchOutput = await ExecuteKmAsync($"search \"content:summaries\" --config {_configPath} --format json").ConfigureAwait(false);
        var searchResult = JsonSerializer.Deserialize<JsonElement>(searchOutput);

        // Assert: Should find "summary" via stemming
        Assert.Equal(1, searchResult.GetProperty("totalResults").GetInt32());
        Assert.Equal(id, searchResult.GetProperty("results")[0].GetProperty("id").GetString());
    }


    [Fact]
    public async Task Process_MongoJsonQuery_FindsCorrectResults()
    {
        // Arrange
        var id1 = JsonSerializer.Deserialize<JsonElement>(
                await ExecuteKmAsync($"put \"kubernetes guide\" --config {_configPath}").ConfigureAwait(false)
            )
            .GetProperty("id")
            .GetString();

        await ExecuteKmAsync($"put \"docker guide\" --config {_configPath}").ConfigureAwait(false);

        // Act: MongoDB JSON format - escape quotes for process arguments
        const string jsonQuery = "{\\\"content\\\": \\\"kubernetes\\\"}";
        var output = await ExecuteKmAsync($"search \"{jsonQuery}\" --config {_configPath} --format json").ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(output);

        // Assert
        Assert.Equal(1, result.GetProperty("totalResults").GetInt32());
        Assert.Equal(id1, result.GetProperty("results")[0].GetProperty("id").GetString());
    }


    [Fact]
    public async Task Process_DefaultMinRelevance_FindsResults()
    {
        // Regression test for BM25 normalization bug

        // Arrange
        await ExecuteKmAsync($"put \"test content\" --config {_configPath}").ConfigureAwait(false);

        // Act: Don't specify min-relevance - use default 0.3
        var output = await ExecuteKmAsync($"search \"test\" --config {_configPath} --format json").ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(output);

        // Assert: Should find results despite default MinRelevance=0.3
        Assert.True(result.GetProperty("totalResults").GetInt32() > 0, "BM25 bug: default MinRelevance filters all results!");

        var relevance = result.GetProperty("results")[0].GetProperty("relevance").GetSingle();
        Assert.True(relevance >= 0.3f, $"Relevance {relevance} below 0.3 threshold");
    }


    [Fact]
    public async Task Process_ComplexNestedQuery_FindsCorrectMatches()
    {
        // Arrange
        var id1 = JsonSerializer.Deserialize<JsonElement>(
                await ExecuteKmAsync($"put \"docker kubernetes guide\" --config {_configPath}").ConfigureAwait(false)
            )
            .GetProperty("id")
            .GetString();

        var id2 = JsonSerializer.Deserialize<JsonElement>(
                await ExecuteKmAsync($"put \"docker helm charts\" --config {_configPath}").ConfigureAwait(false)
            )
            .GetProperty("id")
            .GetString();

        await ExecuteKmAsync($"put \"ansible automation\" --config {_configPath}").ConfigureAwait(false);

        // Act: Nested query
        var output = await ExecuteKmAsync($"search \"docker AND (kubernetes OR helm)\" --config {_configPath} --format json").ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<JsonElement>(output);

        // Assert
        Assert.Equal(2, result.GetProperty("totalResults").GetInt32());
        var ids = result.GetProperty("results")
            .EnumerateArray()
            .Select(r => r.GetProperty("id").GetString())
            .ToHashSet();

        Assert.Contains(id1, ids);
        Assert.Contains(id2, ids);
    }


    private sealed class OllamaFactAttribute : FactAttribute
    {
        public OllamaFactAttribute()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("OLLAMA_AVAILABLE"), "false", StringComparison.OrdinalIgnoreCase))
            {
                Skip = "Skipping because OLLAMA_AVAILABLE=false (vector embeddings unavailable).";
            }
        }
    }
}
