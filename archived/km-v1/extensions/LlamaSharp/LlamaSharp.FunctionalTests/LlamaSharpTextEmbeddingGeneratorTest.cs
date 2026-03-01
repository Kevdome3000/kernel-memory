// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.LlamaSharp;
using Microsoft.KM.TestHelpers;

namespace Microsoft.LlamaSharp.FunctionalTests;

public sealed class LlamaSharpTextEmbeddingGeneratorTest : BaseFunctionalTestCase
{
    private readonly LlamaSharpTextEmbeddingGenerator _target;


    public LlamaSharpTextEmbeddingGeneratorTest(
        IConfiguration cfg,
        ITestOutputHelper output) : base(cfg, output)
    {
        LlamaSharpConfig.Validate();
        _target = new LlamaSharpTextEmbeddingGenerator(LlamaSharpConfig.EmbeddingModel, loggerFactory: null);
        var modelFilename = LlamaSharpConfig.TextModel.ModelPath.Split('/').Last().Split('\\').Last();
        Console.WriteLine($"Model in use: {modelFilename}");
    }


    [Fact]
    [Trait("Category", "LlamaSharp")]
    public async Task ItGeneratesEmbeddingVectors()
    {
        // Act
        Embedding embedding = await _target.GenerateEmbeddingAsync("some text");

        // Assert
        Console.WriteLine("Embedding size: " + embedding.Length);

        // Expected result using nomic-embed-text-v1.5.Q8_0.gguf
        Assert.Equal(768, embedding.Length);
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _target.Dispose();
        }

        base.Dispose(disposing);
    }
}
