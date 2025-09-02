// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory.MemoryDb.AzureAISearch;
using Microsoft.KernelMemory.MemoryDb.Elasticsearch;
using Microsoft.KernelMemory.MemoryDb.Qdrant;
using Microsoft.KernelMemory.MemoryDb.Redis;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.MongoDbAtlas;
using Microsoft.KernelMemory.Postgres;
using Microsoft.KM.TestHelpers;
using StackExchange.Redis;

namespace Microsoft.KM.Core.FunctionalTests.VectorDbComparison;

public class TestMemoryFilters : BaseFunctionalTestCase
{
    private const string IndexName = "test-filters";

    // On/Off toggles
    private readonly bool _azSearchEnabled = true;
    private readonly bool _postgresEnabled = true;
    private readonly bool _elasticsearchEnabled = false;
    private readonly bool _mongoDbAtlasEnabled = false;
    private readonly bool _qdrantEnabled = false;
    private readonly bool _redisEnabled = false;

    private readonly Dictionary<string, IMemoryDb> _memoryDbs = [];


    public TestMemoryFilters(IConfiguration cfg, ITestOutputHelper log) : base(cfg, log)
    {
        FakeEmbeddingGenerator _ = new();

        _memoryDbs.Add("simple", new SimpleVectorDb(SimpleVectorDbConfig, _));

        if (_azSearchEnabled) { _memoryDbs.Add("acs", new AzureAISearchMemory(AzureAiSearchConfig, _)); }

        if (_mongoDbAtlasEnabled) { _memoryDbs.Add("mongoDb", new MongoDbAtlasMemory(MongoDbAtlasConfig, _)); }

        if (_postgresEnabled) { _memoryDbs.Add("postgres", new PostgresMemory(PostgresConfig, _)); }

        if (_qdrantEnabled) { _memoryDbs.Add("qdrant", new QdrantMemory(QdrantConfig, _)); }

        if (_elasticsearchEnabled) { _memoryDbs.Add("es", new ElasticsearchMemory(ElasticsearchConfig, _)); }

        if (_redisEnabled)
        {
            // TODO: revisit RedisMemory not to need this, e.g. not to connect in ctor
            var redisMux = ConnectionMultiplexer.ConnectAsync(RedisConfig.ConnectionString);
            redisMux.Wait(TimeSpan.FromSeconds(5));
            _memoryDbs.Add("redis", new RedisMemory(RedisConfig, redisMux.Result, _));
        }
    }


    [Fact]
    [Trait("Category", "Serverless")]
    public async Task TestFilters()
    {
        // Booleans used for investigating test failures
        const bool DeleteIndex = true;
        const bool CreateIndex = true;
        const bool CreateRecords = true;

        var records = new Dictionary<string, MemoryRecord>
        {
            ["1"] = new() { Id = "1", Vector = new[] { 0.25f, 0.33f, 0.29f }, Tags = new TagCollection { { "user", "Kaylee" }, { "collection", "Work" } } },
            ["2"] = new() { Id = "2", Vector = new[] { 0.25f, 0.25f, 0.35f }, Tags = new TagCollection { { "user", "Kaylee" }, { "collection", "Personal" } } },
            ["3"] = new() { Id = "3", Vector = new[] { 0.1f, 0.1f, 0.1f }, Tags = new TagCollection { { "user", "Kaylee" }, { "collection", "Family" } } },
            ["4"] = new() { Id = "4", Vector = new[] { 0.05f, 0.91f, 0.03f }, Tags = new TagCollection { { "user", "Kaylee" }, { "collection", "Family" } } },
            ["5"] = new() { Id = "5", Vector = new[] { 0.65f, 0.12f, 0.99f }, Tags = new TagCollection { { "user", "Kaylee" }, { "collection", "Family" } } },
            ["6"] = new() { Id = "6", Vector = new[] { 0.81f, 0.12f, 0.13f }, Tags = new TagCollection { { "user", "Madelynn" }, { "collection", "Personal" } } },
            ["7"] = new() { Id = "7", Vector = new[] { 0.88f, 0.01f, 0.13f }, Tags = new TagCollection { { "user", "Madelynn" }, { "collection", "Work" } } }
        };

        if (DeleteIndex) { await DeleteIndexAsync(IndexName); }

        if (CreateIndex) { await CreateIndexAsync(IndexName, 3); }

        if (CreateRecords) { await UpsertAsync(IndexName, records); }

        for (int i = 1; i <= 3; i++)
        {
            Console.WriteLine("\n----- Simple vector DB -----");
            await TestVectorDbFiltering(_memoryDbs["simple"], i);

            if (_memoryDbs.TryGetValue("acs", out IMemoryDb? acs))
            {
                Console.WriteLine("----- Azure AI Search -----");
                await TestVectorDbFiltering(acs, i);
            }

            if (_memoryDbs.TryGetValue("qdrant", out IMemoryDb? qdrant))
            {
                Console.WriteLine("\n----- Qdrant vector DB -----");
                await TestVectorDbFiltering(qdrant, i);
            }

            if (_memoryDbs.TryGetValue("postgres", out IMemoryDb? postgres))
            {
                Console.WriteLine("\n----- Postgres vector DB -----");
                await TestVectorDbFiltering(postgres, i);
            }

            if (_memoryDbs.TryGetValue("mongoDb", out IMemoryDb? mongoDb))
            {
                Console.WriteLine("\n----- MongoDB Atlas vector DB -----");
                await TestVectorDbFiltering(mongoDb, i);
            }

            if (_memoryDbs.TryGetValue("es", out IMemoryDb? es))
            {
                Console.WriteLine("\n----- Elasticsearch vector DB -----");
                await TestVectorDbFiltering(es, i);
            }

            Console.WriteLine("\n\n");
        }
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


    // NOTE: result order does not matter, checking result count only
    private async Task TestVectorDbFiltering(IMemoryDb vectorDb, int test)
    {
        // Single memory filter
        if (test == 1)
        {
            var singleFilter = new List<MemoryFilter> { MemoryFilters.ByTag("user", "Kaylee") };
            var singleFilterResults = await vectorDb.GetListAsync(IndexName, singleFilter, int.MaxValue).ToListAsync();
            Console.WriteLine($"\nSingle memory filter: {singleFilterResults.Count} results");

            foreach (MemoryRecord r in singleFilterResults.OrderBy(x => x.Id))
            {
                Console.WriteLine($" - ID: {r.Id}, Tags: {string.Join(", ", r.Tags.Select(t => $"{t.Key}: {string.Join(", ", t.Value)}"))}");
            }

            Assert.Equal(5, singleFilterResults.Count);
        }

        // Single memory filter with multiple tags
        if (test == 2)
        {
            var singleFilterMultipleTags = new List<MemoryFilter> { MemoryFilters.ByTag("user", "Kaylee").ByTag("collection", "Work") };
            var singleFilterMultipleTagsResults = await vectorDb.GetListAsync(IndexName, singleFilterMultipleTags, int.MaxValue).ToListAsync();
            Console.WriteLine($"\nSingle memory filter with multiple tags: {singleFilterMultipleTagsResults.Count} results");

            foreach (MemoryRecord r in singleFilterMultipleTagsResults.OrderBy(x => x.Id))
            {
                Console.WriteLine($" - ID: {r.Id}, Tags: {string.Join(", ", r.Tags.Select(t => $"{t.Key}: {string.Join(", ", t.Value)}"))}");
            }

            Assert.Equal(1, singleFilterMultipleTagsResults.Count);
        }

        // Multiple memory filters with multiple tags
        if (test == 3)
        {
            var multipleFilters = new List<MemoryFilter>
            {
                MemoryFilters.ByTag("user", "Kaylee").ByTag("collection", "Family"),
                MemoryFilters.ByTag("user", "Madelynn").ByTag("collection", "Personal")
            };
            var multipleFiltersResults = await vectorDb.GetListAsync(IndexName, multipleFilters, int.MaxValue).ToListAsync();
            Console.WriteLine($"\nMultiple memory filters with multiple tags: {multipleFiltersResults.Count} results");

            foreach (MemoryRecord r in multipleFiltersResults.OrderBy(x => x.Id))
            {
                Console.WriteLine($" - ID: {r.Id}, Tags: {string.Join(", ", r.Tags.Select(t => $"{t.Key}: {string.Join(", ", t.Value)}"))}");
            }

            Assert.Equal(4, multipleFiltersResults.Count);
        }
    }
}
