// Copyright (c) Microsoft.All rights reserved.

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.AI.LlamaSharp;
using Microsoft.KM.TestHelpers;

namespace Microsoft.LlamaSharp.FunctionalTests;

public sealed class LlamaSharpTextGeneratorTest : BaseFunctionalTestCase
{
    private readonly LlamaSharpTextGenerator _target;
    private readonly Stopwatch _timer;


    public LlamaSharpTextGeneratorTest(
        IConfiguration cfg,
        ITestOutputHelper output) : base(cfg, output)
    {
        _timer = new Stopwatch();

        LlamaSharpConfig.Validate();
        _target = new LlamaSharpTextGenerator(LlamaSharpConfig.TextModel, loggerFactory: null);
        var modelFilename = LlamaSharpConfig.TextModel.ModelPath.Split('/').Last().Split('\\').Last();
        Console.WriteLine($"Model in use: {modelFilename}");
    }


    [Fact]
    [Trait("Category", "LlamaSharp")]
    public void ItCountsTokens()
    {
        // Arrange
        var text = "hello world, we can run llama";

        // Act
        _timer.Restart();
        var tokenCount = _target.CountTokens(text);
        _timer.Stop();

        // Assert
        Console.WriteLine("Phi3 token count: " + tokenCount);
        Console.WriteLine("GPT4 token count: " + new CL100KTokenizer().CountTokens(text));
        Console.WriteLine($"Time: {_timer.ElapsedMilliseconds / 1000} secs");

        // Expected result with Phi-3-mini-4k-instruct-q4.gguf, without BoS (https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf)
        Assert.Equal(8, tokenCount);
    }


    [Fact]
    [Trait("Category", "LlamaSharp")]
    public void ItCountsTokensOfEmptyStrings()
    {
        // Act - No Exceptions should occur
        _target.CountTokens("");
        _target.CountTokens("\r");

        // Make sure these don't throw an exception
        // See https://github.com/SciSharp/LLamaSharp/issues/430
        _target.CountTokens("\n");
        _target.CountTokens("\n\n");
        _target.CountTokens("\t");
        _target.CountTokens("\t\t");
        _target.CountTokens("\v");
        _target.CountTokens("\v\v");
        _target.CountTokens("\0");
        _target.CountTokens("\0\0");
        _target.CountTokens("\b");
        _target.CountTokens("\b\b");
    }


    [Fact]
    [Trait("Category", "LlamaSharp")]
    public async Task ItGeneratesText()
    {
        // Arrange
        var prompt = """
                     # Current date: 12/12/2024.
                     # Instructions: use JSON syntax.
                     # Deduction: { "DayOfWeek": "Monday", "MonthName":
                     """;
        var options = new TextGenerationOptions
        {
            MaxTokens = 60,
            Temperature = 0,
            StopSequences = ["Question"]
        };

        // Act
        _timer.Restart();
        var tokens = _target.GenerateTextAsync(prompt, options);
        var result = new StringBuilder();

        await foreach (var token in tokens)
        {
            result.Append(token);
        }

        _timer.Stop();
        var answer = result.ToString();

        // Assert
        Console.WriteLine($"Model Output:\n=============================\n{answer}\n=============================");
        Console.WriteLine($"Time: {_timer.ElapsedMilliseconds / 1000} secs");
        Assert.Contains("december", answer, StringComparison.OrdinalIgnoreCase);
    }


    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _target.Dispose();
    }
}
