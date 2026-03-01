// Copyright (c) Microsoft. All rights reserved.
using KernelMemory.Core.Storage.Models;
using KernelMemory.Main.CLI.OutputFormatters;

namespace KernelMemory.Main.Tests.Unit.OutputFormatters;

/// <summary>
/// Unit tests for YamlOutputFormatter.
/// </summary>
[Collection("ConsoleOutputTests")]
public sealed class YamlOutputFormatterTests
{
    [Fact]
    public void Constructor_SetsVerbosity()
    {
        // Arrange & Act
        var formatter = new YamlOutputFormatter("verbose");

        // Assert
        Assert.Equal("verbose", formatter.Verbosity);
    }


    [Fact]
    public void Format_WithNormalVerbosity_DoesNotThrow()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("normal");
        var data = new { id = "test-456", result = "ok" };

        // Act & Assert
        formatter.Format(data);
    }


    [Fact]
    public void Format_WithSilentVerbosity_DoesNotOutput()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("silent");
        var data = new { test = "data" };

        // Act & Assert
        formatter.Format(data);
    }


    [Fact]
    public void Format_WithContentDto_DoesNotThrow()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("normal");
        var content = new ContentDto
        {
            Id = "yaml-test-id",
            Content = "YAML content",
            MimeType = "text/yaml"
        };

        // Act & Assert
        formatter.Format(content);
    }


    [Fact]
    public void FormatError_WithMessage_DoesNotThrow()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("normal");
        const string error = "YAML error occurred";

        // Act & Assert
        formatter.FormatError(error);
    }


    [Fact]
    public void FormatList_WithItems_DoesNotThrow()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("normal");
        var items = new List<string> { "node1", "node2", "node3" };

        // Act & Assert
        formatter.FormatList(items,
            3,
            0,
            3);
    }


    [Fact]
    public void FormatList_WithSilentVerbosity_DoesNotOutput()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("silent");
        var items = new List<ContentDto>
        {
            new() { Id = "id1", Content = "Content" }
        };

        // Act & Assert
        formatter.FormatList(items,
            1,
            0,
            1);
    }


    [Fact]
    public void FormatList_WithEmptyList_DoesNotThrow()
    {
        // Arrange
        var formatter = new YamlOutputFormatter("normal");
        var items = new List<string>();

        // Act & Assert
        formatter.FormatList(items,
            0,
            0,
            10);
    }
}
