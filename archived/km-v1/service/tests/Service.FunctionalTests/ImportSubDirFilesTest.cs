// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.KM.Service.FunctionalTests;

public class ImportSubDirFilesTest : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;
    private readonly string? _fixturesPath;


    public ImportSubDirFilesTest(
        IConfiguration cfg,
        ITestOutputHelper output) : base(cfg, output)
    {
        _fixturesPath = FindFixturesDir();
        Assert.NotNull(_fixturesPath);
        Console.WriteLine($"\n# Fixtures directory found: {_fixturesPath}");

        _memory = GetMemoryWebClient();
    }


    [Fact]
    [Trait("Category", "WebService")]
    public async Task ItImportsFromSubDirsApi1()
    {
        // Act - Assert no exception occurs
        await _memory.ImportDocumentAsync(
            Path.Join(_fixturesPath, "Doc1.txt"),
            "Doc1.txt",
            steps: ["extract", "partition"]);

        await _memory.ImportDocumentAsync(
            Path.Join(_fixturesPath, "Documents", "Doc1.txt"),
            "Documents-Doc1.txt",
            steps: ["extract", "partition"]);
    }


    [Fact]
    [Trait("Category", "WebService")]
    public async Task ItImportsFromSubDirsApi2()
    {
        // Act - Assert no exception occurs
        await _memory.ImportDocumentAsync(
            new Document("Doc2.txt")
                .AddFile(Path.Join(_fixturesPath, "Doc1.txt"))
                .AddFile(Path.Join(_fixturesPath, "Documents", "Doc1.txt")),
            steps: ["extract", "partition"]);
    }
}
