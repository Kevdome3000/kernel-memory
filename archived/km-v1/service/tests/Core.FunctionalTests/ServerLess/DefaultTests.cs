// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KM.Core.FunctionalTests.DefaultTestCases;
using Microsoft.KM.TestHelpers;

namespace Microsoft.KM.Core.FunctionalTests.ServerLess;

public class DefaultTests : BaseFunctionalTestCase
{
    public DefaultTests(IConfiguration cfg, ITestOutputHelper output) : base(cfg, output)
    {
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItSupportsASingleFilter(string memoryType)
    {
        await FilteringTest.ItSupportsASingleFilter(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItSupportsMultipleFilters(string memoryType)
    {
        await FilteringTest.ItSupportsMultipleFilters(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItIgnoresEmptyFilters(string memoryType)
    {
        await FilteringTest.ItIgnoresEmptyFilters(GetServerlessMemory(memoryType), Log, true);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItListsIndexes(string memoryType)
    {
        await IndexListTest.ItListsIndexes(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItNormalizesIndexNames(string memoryType)
    {
        await IndexListTest.ItNormalizesIndexNames(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItDeletesIndexes(string memoryType)
    {
        await IndexDeletionTest.ItDeletesIndexes(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItHandlesMissingIndexesConsistently(string memoryType)
    {
        await MissingIndexTest.ItHandlesMissingIndexesConsistently(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItUploadsPDFDocsAndDeletes(string memoryType)
    {
        await DocumentUploadTest.ItUploadsPDFDocsAndDeletes(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItSupportsTags(string memoryType)
    {
        await DocumentUploadTest.ItSupportsTags(GetServerlessMemory(memoryType), Log);
    }


    [Theory]
    [Trait("Category", "Serverless")]
    [InlineData("default")]
    [InlineData("simple_on_disk")]
    [InlineData("simple_volatile")]
    public async Task ItDownloadsPDFDocs(string memoryType)
    {
        await DocumentUploadTest.ItDownloadsPDFDocs(GetServerlessMemory(memoryType), Log);
    }
}
