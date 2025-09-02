// Copyright (c) Microsoft.All rights reserved.

using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory.DocumentStorage;
using Microsoft.KernelMemory.MongoDbAtlas;
using Microsoft.KernelMemory.MongoDbAtlas.Internals;
using Microsoft.KM.TestHelpers;

namespace Microsoft.MongoDbAtlas.FunctionalTests;

public class StorageTestsSingleCollection : StorageTests
{
    public StorageTestsSingleCollection(IConfiguration cfg, ITestOutputHelper output)
        : base(cfg, output, false)
    {
    }
}


public class StorageTestsMultipleCollections : StorageTests
{
    public StorageTestsMultipleCollections(IConfiguration cfg, ITestOutputHelper output)
        : base(cfg, output, true)
    {
    }
}


public abstract class StorageTests : BaseFunctionalTestCase
{
    private readonly MongoDbAtlasStorage _sut;
    private readonly string IndexName = $"storagetestindex{_seed++}";
    private static int _seed = 0;


    protected StorageTests(IConfiguration cfg, ITestOutputHelper output, bool multiCollection) : base(cfg, output)
    {
        if (multiCollection)
        {
            MongoDbAtlasConfig.DatabaseName += "StorageTestsMultiCollection";
            MongoDbAtlasConfig.UseSingleCollectionForVectorSearch = false;
        }
        else
        {
            MongoDbAtlasConfig.DatabaseName += "StorageTests";
            MongoDbAtlasConfig.UseSingleCollectionForVectorSearch = true;
        }

        var ash = new MongoDbAtlasSearchHelper(MongoDbAtlasConfig.ConnectionString, MongoDbAtlasConfig.DatabaseName);

        // delete everything for every collection
        ash.DropAllDocumentsFromCollectionsAsync().Wait();

        _sut = new MongoDbAtlasStorage(MongoDbAtlasConfig);
        _sut.CreateIndexDirectoryAsync("testindex").Wait();
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            base.Dispose(disposing);
            _sut.DeleteIndexDirectoryAsync(IndexName).Wait();
        }
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task SaveFilesHonorsId()
    {
        // Act save a file with the same id updating content.
        var fileContent1 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World"));
        var fileContent2 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World 2"));
        string id = $"_pipeline_status{_seed++}.txt";
        await _sut.WriteFileAsync(IndexName,
            id,
            "filename.txt",
            fileContent1);
        await _sut.WriteFileAsync(IndexName,
            id,
            "filename.txt",
            fileContent2);

        // Assert
        var file = await _sut.ReadFileAsync(IndexName, id, "filename.txt");
        var content = file.ToString();

        Assert.Equal("Hello World 2", content);
    }


    [Theory]
    [Trait("Category", "MongoDbAtlas")]
    [InlineData("txt", "Hello World", "Hello world 2")]
    [InlineData("text_embedding", @"{ ""Text"": ""Hello World"" }", @"{ ""Text"": ""Hello World 2"" }")]
    public async Task SaveDifferentFiles(string extension, string content1, string content2)
    {
        // Act save a file with the same id updating content.
        var fileContent1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
        var fileContent2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));
        string id = $"_pipeline_status{_seed++}.{extension}";
        string fileName1 = $"filename{_seed++}.{extension}";
        string fileName2 = $"filename{_seed++}.{extension}";

        await _sut.WriteFileAsync(IndexName,
            id,
            fileName1,
            fileContent1);
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName2,
            fileContent2);

        // Assert
        var file = await _sut.ReadFileAsync(IndexName, id, fileName1);
        var content = file.ToString();

        Assert.Equal(content1, content);

        file = await _sut.ReadFileAsync(IndexName, id, fileName2);
        content = file.ToString();
        Assert.Equal(content2, content);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task SaveFilesHonorsIdWithBinaryContent()
    {
        // Act save a file with the same id updating content.
        var fileContent1 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World"));
        var fileContent2 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World 2"));
        string id = $"_pipeline_status{_seed++}.txt";
        var fileName = $"filename{_seed++}.bin";
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName,
            fileContent1);
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName,
            fileContent2);

        // Assert
        var file = await _sut.ReadFileAsync(IndexName, id, fileName);
        var content = file.ToString();

        Assert.Equal("Hello World 2", content);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task SaveDifferentFilesWithBinaryContent()
    {
        // Act save a file with the same id updating content.
        var fileContent1 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World"));
        var fileContent2 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World 2"));
        string id = $"_pipeline_status{_seed++}.txt";
        var fileName1 = $"filename{_seed++}.bin";
        var fileName2 = $"filename{_seed++}.bin";
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName1,
            fileContent1);
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName2,
            fileContent2);

        // Assert
        var file = await _sut.ReadFileAsync(IndexName, id, fileName1);
        var content = file.ToString();

        Assert.Equal("Hello World", content);

        file = await _sut.ReadFileAsync(IndexName, id, fileName2);
        content = file.ToString();
        Assert.Equal("Hello World 2", content);
    }


    [Fact]
    [Trait("Category", "MongoDbAtlas")]
    public async Task CanCleanIndexCorrectly()
    {
        // Arrange: save some files into the index
        var fileContent1 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World"));
        var fileContent2 = new MemoryStream(Encoding.UTF8.GetBytes("Hello World 2"));
        string id = $"_pipeline_status{_seed++}.txt";
        var fileName1 = $"filename{_seed++}.txt";
        var fileName2 = $"filename{_seed++}.bin";
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName1,
            fileContent1);
        await _sut.WriteFileAsync(IndexName,
            id,
            fileName2,
            fileContent2);

        // Act: clean the index
        await _sut.EmptyDocumentDirectoryAsync(IndexName, id);

        // Assert: check that the files are not there anymore
        await Assert.ThrowsAsync<DocumentStorageFileNotFoundException>(async () => await _sut.ReadFileAsync(IndexName,
            id,
            fileName1,
            false));
        await Assert.ThrowsAsync<DocumentStorageFileNotFoundException>(async () => await _sut.ReadFileAsync(IndexName,
            id,
            fileName2,
            false));
    }
}
