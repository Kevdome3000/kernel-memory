// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryDb.AzureAISearch;
using Microsoft.KernelMemory.MemoryDb.Elasticsearch;
using Microsoft.KernelMemory.MemoryDb.Qdrant;
using Microsoft.KernelMemory.MemoryDb.Redis;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.MongoDbAtlas;
using Microsoft.KernelMemory.Postgres;
using Microsoft.KM.TestHelpers;
using StackExchange.Redis;

namespace Microsoft.KM.Core.FunctionalTests.VectorDbComparison;

public class TestCosineSimilarity : BaseFunctionalTestCase
{
    private const string IndexName = "test-cosinesimil";

    // On/Off toggles
    private readonly bool _azSearchEnabled = true;
    private readonly bool _postgresEnabled = true;
    private readonly bool _elasticsearchEnabled = false;
    private readonly bool _mongoDbAtlasEnabled = false;
    private readonly bool _qdrantEnabled = false;
    private readonly bool _redisEnabled = false;

    private readonly Dictionary<string, IMemoryDb> _memoryDbs = [];
    private readonly FakeEmbeddingGenerator _embeddingGenerator;


    public TestCosineSimilarity(IConfiguration cfg, ITestOutputHelper log) : base(cfg, log)
    {
        _embeddingGenerator = new FakeEmbeddingGenerator();

        _memoryDbs.Add("simple", new SimpleVectorDb(SimpleVectorDbConfig, _embeddingGenerator));

        if (_azSearchEnabled)
        {
            AzureAiSearchConfig.UseHybridSearch = false;
            _memoryDbs.Add("acs", new AzureAISearchMemory(AzureAiSearchConfig, _embeddingGenerator));
        }

        if (_mongoDbAtlasEnabled) { _memoryDbs.Add("mongoDb", new MongoDbAtlasMemory(MongoDbAtlasConfig, _embeddingGenerator)); }

        if (_postgresEnabled) { _memoryDbs.Add("postgres", new PostgresMemory(PostgresConfig, _embeddingGenerator)); }

        if (_qdrantEnabled) { _memoryDbs.Add("qdrant", new QdrantMemory(QdrantConfig, _embeddingGenerator)); }

        if (_elasticsearchEnabled) { _memoryDbs.Add("es", new ElasticsearchMemory(ElasticsearchConfig, _embeddingGenerator)); }

        if (_redisEnabled)
        {
            // TODO: revisit RedisMemory not to need this, e.g. not to connect in ctor
            var redisMux = ConnectionMultiplexer.ConnectAsync(RedisConfig.ConnectionString);
            redisMux.Wait(TimeSpan.FromSeconds(5));
            _memoryDbs.Add("redis", new RedisMemory(RedisConfig, redisMux.Result, _embeddingGenerator));
        }
    }


    [Fact]
    [Trait("Category", "Serverless")]
    public async Task CompareCosineSimilarity()
    {
        var target = new[] { 0.01f, 0.5f, 0.41f };
        _embeddingGenerator.Mock("text01", target);

        // == Delete indexes left over
        await DeleteIndexAsync(IndexName);

        // == Create indexes
        await CreateIndexAsync(IndexName, 3);

        // == Insert data. Note: records are inserted out of order on purpose.
        var records = new Dictionary<string, MemoryRecord>
        {
            ["3"] = new() { Id = "3", Vector = new[] { 0.1f, 0.1f, 0.1f } },
            ["2"] = new() { Id = "2", Vector = new[] { 0.25f, 0.25f, 0.35f } },
            ["1"] = new() { Id = "1", Vector = new[] { 0.25f, 0.33f, 0.29f } },
            ["5"] = new() { Id = "5", Vector = new[] { 0.65f, 0.12f, 0.99f } },
            ["4"] = new() { Id = "4", Vector = new[] { 0.05f, 0.91f, 0.03f } },
            ["7"] = new() { Id = "7", Vector = new[] { 0.88f, 0.01f, 0.13f } },
            ["6"] = new() { Id = "6", Vector = new[] { 0.81f, 0.12f, 0.13f } }
        };
        await UpsertAsync(IndexName, records);

        // == Test results: test precision and ordering
        await TestSimilarityAsync(records);
    }


    private async Task DeleteIndexAsync(string indexName)
    {
        foreach (var memoryDb in _memoryDbs)
        {
            Console.WriteLine($"Deleting index {indexName} in {memoryDb.Value.GetType().FullName}");
            await memoryDb.Value.DeleteIndexAsync(indexName);
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }


    private async Task CreateIndexAsync(string indexName, int vectorSize)
    {
        foreach (var memoryDb in _memoryDbs)
        {
            Console.WriteLine($"Creating index {indexName} in {memoryDb.Value.GetType().FullName}");
            await memoryDb.Value.CreateIndexAsync(indexName, vectorSize);
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }


    private async Task UpsertAsync(string indexName, Dictionary<string, MemoryRecord> records)
    {
        foreach (KeyValuePair<string, MemoryRecord> record in records)
        {
            foreach (var memoryDb in _memoryDbs)
            {
                Console.WriteLine($"Adding record in {memoryDb.Value.GetType().FullName}");
                await memoryDb.Value.UpsertAsync(indexName, record.Value);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }


    private async Task TestSimilarityAsync(Dictionary<string, MemoryRecord> records)
    {
        var target = new[] { 0.01f, 0.5f, 0.41f };

        foreach (var memoryDb in _memoryDbs)
        {
            const double Precision = 0.000001d;
            var previous = "0";

            IAsyncEnumerable<(MemoryRecord, double)> list = memoryDb.Value.GetSimilarListAsync(
                IndexName,
                "text01",
                limit: 10,
                withEmbeddings: true);
            List<(MemoryRecord, double)> results = await list.ToListAsync();

            Console.WriteLine($"\n\n{memoryDb.Value.GetType().FullName}: {results.Count} results");
            previous = "0";

            foreach ((MemoryRecord? memoryRecord, double actual) in results)
            {
                var expected = CosineSim(target, records[memoryRecord.Id].Vector);
                var diff = expected - actual;
                Console.WriteLine($" - ID: {memoryRecord.Id}, Distance: {actual}, Expected distance: {expected}, Difference: {diff:0.0000000000}");
                Assert.True(Math.Abs(diff) < Precision);
                Assert.True(string.Compare(memoryRecord.Id, previous, StringComparison.OrdinalIgnoreCase) > 0, "Records are not ordered by similarity");
                previous = memoryRecord.Id;
            }
        }
    }


    // Note: not using external libraries to have complete control on the expected value.
    private static double CosineSim(Embedding vec1, Embedding vec2)
    {
        var v1 = vec1.Data.ToArray();
        var v2 = vec2.Data.ToArray();

        if (vec1.Length != vec2.Length)
        {
            throw new Exception($"Vector size should be the same: {vec1.Length} != {vec2.Length}");
        }

        int size = vec1.Length;
        double dot = 0.0d;
        double m1 = 0.0d;
        double m2 = 0.0d;

        for (int n = 0; n < size; n++)
        {
            dot += v1[n] * v2[n];
            m1 += Math.Pow(v1[n], 2);
            m2 += Math.Pow(v2[n], 2);
        }

        double cosineSimilarity = dot / (Math.Sqrt(m1) * Math.Sqrt(m2));
        return cosineSimilarity;
    }
}
