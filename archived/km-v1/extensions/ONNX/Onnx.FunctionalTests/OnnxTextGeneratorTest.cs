// Copyright (c) Microsoft.All rights reserved.

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.AI.Onnx;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Onnx.FunctionalTests;

public sealed class OnnxTextGeneratorTest : BaseFunctionalTestCase
{
    private readonly OnnxTextGenerator _target;
    private readonly Stopwatch _timer;


    public OnnxTextGeneratorTest(
        IConfiguration cfg,
        ITestOutputHelper output) : base(cfg, output)
    {
        _timer = new Stopwatch();

        OnnxConfig.Validate();
        _target = new OnnxTextGenerator(OnnxConfig, loggerFactory: null);

        var modelDirectory = Path.GetFullPath(OnnxConfig.TextModelDir);
        var modelFile = Directory.GetFiles(modelDirectory)
            .FirstOrDefault(file => string.Equals(Path.GetExtension(file), ".ONNX", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Using model {Path.GetFileNameWithoutExtension(modelFile)} from: {modelDirectory}");
    }


    [Fact]
    [Trait("Category", "Onnx")]
    public async Task ItGeneratesText()
    {
        var utcDate = DateTime.UtcNow.Date.ToString("MM/dd/yyyy");
        var systemPrompt = $"Following the format \"MM/dd/yyyy\", the current date is {utcDate}.";
        var question = "What is the current date?";
        var prompt = $"<|system|>{systemPrompt}<|end|><|user|>{question}<|end|><|assistant|>";

        var options = new TextGenerationOptions();

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
        Assert.Contains(utcDate, answer, StringComparison.OrdinalIgnoreCase);
    }


    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _target.Dispose();
    }
}
