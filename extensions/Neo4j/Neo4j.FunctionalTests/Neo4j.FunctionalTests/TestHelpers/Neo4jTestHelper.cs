// Copyright (c) Microsoft.All rights reserved.

using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.KernelMemory;
using Neo4j.Driver;

namespace Microsoft.Neo4j.FunctionalTests.TestHelpers;

/// <summary>
/// Test utilities and helpers specific to Neo4j connector testing.
/// Provides database cleanup, isolation strategies, and performance benchmarking.
/// </summary>
internal static class Neo4jTestHelper
{
    public const string WikipediaCarbonFileName = "Data/file1-Wikipedia-Carbon.txt";
    public const string WikipediaMoonFilename = "Data/file2-Wikipedia-Moon.txt";
    public const string LoremIpsumFileName = "Data/file3-lorem-ipsum.docx";
    public const string NASANewsFileName = "data/file5-NASA-news.pdf";
    public const string SKReadmeFileName = "Data/file4-SK-Readme.pdf";

    private static readonly Regex s_replaceIndexNameCharsRegex = new(@"[\s\\/._:]", RegexOptions.Compiled);
    private const string ValidSeparator = "-";


    /// <summary>
    /// Creates a Neo4j driver instance for test utilities.
    /// </summary>
    /// <param name="config">Neo4j configuration</param>
    /// <returns>Neo4j driver instance</returns>
    public static IDriver CreateTestDriver(Neo4jConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return GraphDatabase.Driver(new Uri(config.Uri), AuthTokens.Basic(config.Username, config.Password));
    }


    /// <summary>
    /// Normalizes an index name for Neo4j testing (local implementation).
    /// </summary>
    /// <param name="index">The index name to normalize</param>
    /// <returns>Normalized index name</returns>
    public static string NormalizeIndexName(string index)
    {
        if (string.IsNullOrWhiteSpace(index))
        {
            throw new ArgumentNullException(nameof(index), "The index name is empty");
        }

#pragma warning disable CA1308
        index = s_replaceIndexNameCharsRegex.Replace(index.Trim().ToLowerInvariant(), ValidSeparator);
#pragma warning restore CA1308

        return index.Trim();
    }


    /// <summary>
    /// Deletes all test data and indexes created by test methods.
    /// Uses reflection to find test methods and cleans up corresponding indexes.
    /// </summary>
    /// <param name="driver">Neo4j driver instance</param>
    /// <param name="unitTestType">Type of the test class</param>
    /// <param name="config">Neo4j configuration</param>
    /// <returns>List of cleaned up index names</returns>
    public static async Task<IEnumerable<string>> CleanupTestDataAsync(this IDriver driver, Type unitTestType, Neo4jConfig config)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(unitTestType);
        ArgumentNullException.ThrowIfNull(config);

        // Get all test methods from the test class
        MethodInfo[] methods = unitTestType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m =>
                m.GetCustomAttribute<FactAttribute>() != null || m.GetCustomAttribute<TheoryAttribute>() != null)
            .ToArray();

        if (methods.Length == 0)
        {
            throw new ArgumentException($"No public test methods found in class '{unitTestType.Name}'.");
        }

        List<string> cleanedIndexes = [];

        await using IAsyncSession? session = driver.AsyncSession();

        foreach (MethodInfo method in methods)
        {
            string indexName = NormalizeIndexName(method.Name);
            string prefixedIndexName = string.IsNullOrEmpty(config.IndexNamePrefix)
                ? indexName
                : config.IndexNamePrefix + indexName;

            string label = string.IsNullOrEmpty(config.LabelPrefix)
                ? indexName.ToUpperInvariant()
                : config.LabelPrefix + indexName.ToUpperInvariant();

            try
            {
                // Delete vector index
                await session.RunAsync($"DROP INDEX `{prefixedIndexName}` IF EXISTS");

                // Delete all nodes with the test label
                await session.RunAsync($"MATCH (n:{label}) DETACH DELETE n");

                // Drop uniqueness constraint if it exists
                await session.RunAsync($"DROP CONSTRAINT IF EXISTS FOR (n:{label}) REQUIRE n.id IS UNIQUE");

                cleanedIndexes.Add(prefixedIndexName);
            }
            catch (Exception ex)
            {
                // Log but don't fail the cleanup
                Console.WriteLine($"Warning: Failed to cleanup index '{prefixedIndexName}': {ex.Message}");
            }
        }

        return cleanedIndexes;
    }


    /// <summary>
    /// Performs a complete database cleanup, removing all Kernel Memory related data.
    /// Use with caution - this will delete ALL KM data in the database.
    /// </summary>
    /// <param name="driver">Neo4j driver instance</param>
    /// <param name="config">Neo4j configuration</param>
    public static async Task PerformFullCleanupAsync(this IDriver driver, Neo4jConfig config)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(config);

        await using IAsyncSession? session = driver.AsyncSession();

        try
        {
            // Get all vector indexes that match our prefix pattern
            string indexPrefix = config.IndexNamePrefix ?? "";
            string labelPrefix = config.LabelPrefix ?? "";

            // Drop all vector indexes with our prefix
            IResultCursor? indexResult = await session.RunAsync("SHOW VECTOR INDEXES YIELD name");
            List<IRecord>? indexes = await indexResult.ToListAsync();

            foreach (string? indexName in indexes.Select(record => record["name"].As<string>()).Where(indexName => indexName.StartsWith(indexPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                await session.RunAsync($"DROP INDEX `{indexName}` IF EXISTS");
            }

            // Delete all nodes with Memory label or our label prefix
            if (!string.IsNullOrEmpty(labelPrefix))
            {
                await session.RunAsync($"MATCH (n) WHERE any(label IN labels(n) WHERE label STARTS WITH '{labelPrefix}') DETACH DELETE n");
            }

            await session.RunAsync("MATCH (n:Memory) DETACH DELETE n");

            // Drop all constraints for our labels
            IResultCursor? constraintResult = await session.RunAsync("SHOW CONSTRAINTS YIELD name, labelsOrTypes");
            List<IRecord>? constraints = await constraintResult.ToListAsync();

            foreach (string? constraintName in from record in constraints
                                               let constraintName = record["name"].As<string>()
                                               let labels = record["labelsOrTypes"].As<List<object>>()
                                               where Enumerable.Any<object>(labels, l => l.ToString()!.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase) || l.ToString()!.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                                               select constraintName)
            {
                await session.RunAsync($"DROP CONSTRAINT `{constraintName}` IF EXISTS");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Full cleanup encountered errors: {ex.Message}");
            throw;
        }
    }


    /// <summary>
    /// Verifies that Neo4j has vector support enabled.
    /// </summary>
    /// <param name="driver">Neo4j driver instance</param>
    /// <returns>True if vector support is available</returns>
    public static async Task<bool> VerifyVectorSupportAsync(this IDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        await using IAsyncSession? session = driver.AsyncSession();

        try
        {
            // Try to call a vector-related procedure
            await session.RunAsync("CALL db.index.vector.queryNodes('dummy', 1, [0.1, 0.2, 0.3]) YIELD node RETURN count(node) LIMIT 0");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }


    /// <summary>
    /// Creates a unique test index name to avoid conflicts between concurrent tests.
    /// </summary>
    /// <param name="baseIndexName">Base index name</param>
    /// <param name="testMethodName">Name of the test method</param>
    /// <returns>Unique index name</returns>
    public static string CreateUniqueTestIndexName(string baseIndexName, string testMethodName)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string uniqueSuffix = $"{testMethodName}-{timestamp}";
        return NormalizeIndexName($"{baseIndexName}-{uniqueSuffix}");
    }


    /// <summary>
    /// Measures the performance of a Neo4j operation.
    /// </summary>
    /// <param name="operation">Operation to measure</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <returns>Elapsed time in milliseconds</returns>
    public static async Task<long> MeasureOperationAsync(Func<Task> operation, string operationName)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await operation();
            return stopwatch.ElapsedMilliseconds;
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"[PERF] {operationName}: {stopwatch.ElapsedMilliseconds}ms");
        }
    }


    /// <summary>
    /// Measures the performance of a Neo4j operation that returns a result.
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="operation">Operation to measure</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <returns>Tuple of result and elapsed time in milliseconds</returns>
    public static async Task<(T Result, long ElapsedMs)> MeasureOperationAsync<T>(Func<Task<T>> operation, string operationName)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            T result = await operation();
            return (result, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine($"[PERF] {operationName}: {stopwatch.ElapsedMilliseconds}ms");
        }
    }


    /// <summary>
    /// Waits for Neo4j to be ready and responsive.
    /// </summary>
    /// <param name="driver">Neo4j driver instance</param>
    /// <param name="maxWaitTimeMs">Maximum wait time in milliseconds</param>
    /// <returns>True if Neo4j is ready, false if timeout</returns>
    public static async Task<bool> WaitForNeo4jReadyAsync(this IDriver driver, int maxWaitTimeMs = 30000)
    {
        ArgumentNullException.ThrowIfNull(driver);

        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < maxWaitTimeMs)
        {
            try
            {
                await using IAsyncSession? session = driver.AsyncSession();
                await session.RunAsync("RETURN 1");
                return true;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        return false;
    }
}
