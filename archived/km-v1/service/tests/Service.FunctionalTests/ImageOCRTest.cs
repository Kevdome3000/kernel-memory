// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.KM.Service.FunctionalTests;

public class ImageOCRTest : BaseFunctionalTestCase
{
    private readonly IKernelMemory _memory;
    private readonly string? _fixturesPath;


    public ImageOCRTest(
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
    public async Task ItUsesTextFoundInsideImages()
    {
        // Arrange
        const string DocId = nameof(ItUsesTextFoundInsideImages);
        await _memory.ImportDocumentAsync(new Document(DocId)
            .AddFiles([Path.Join(_fixturesPath, "ANWC-image-for-OCR.jpg")]));

        // Wait
        while (!await _memory.IsDocumentReadyAsync(DocId))
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Act
        var answer = await _memory.AskAsync("Who is sponsoring the Automotive News World Congress?");

        // Assert
        Console.WriteLine(answer.Result);
        Assert.Contains("Microsoft", answer.Result);
    }
}
