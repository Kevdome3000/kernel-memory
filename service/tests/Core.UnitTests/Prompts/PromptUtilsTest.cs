// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Prompts;

namespace Microsoft.KM.Core.UnitTests.Prompts;

public class PromptUtilsTest
{
    [Fact]
    [Trait("Category", "UnitTest")]
    public void ItRendersSimpleFactTemplates()
    {
        Assert.Equal("", PromptUtils.RenderFactTemplate("", ""));
        Assert.Equal("x", PromptUtils.RenderFactTemplate("x", "text"));
        Assert.Equal("text", PromptUtils.RenderFactTemplate("{{$content}}", "text"));
        Assert.Equal("\ntext\n", PromptUtils.RenderFactTemplate("\n{{$content}}\n", "text"));
        Assert.Equal("text--", PromptUtils.RenderFactTemplate("{{$content}}-{{$relevance}}-{{$memoryId}}", "text"));
        Assert.Equal("text-0.23-id0",
            PromptUtils.RenderFactTemplate("{{$content}}-{{$relevance}}-{{$memoryId}}",
                "text",
                "src",
                "0.23",
                "id0"));
        Assert.Equal("==== [File:src;Relevance:0.23]:\ntext",
            PromptUtils.RenderFactTemplate("==== [File:{{$source}};Relevance:{{$relevance}}]:\n{{$content}}",
                "text",
                "src",
                "0.23",
                "id0"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    public void ItRendersFactTemplatesWithTags()
    {
        Assert.Equal("text; Foo:-", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$tags[foo]}}", "text"));

        var tags = new TagCollection { "foo" };
        Assert.Equal("text; Foo:-", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$tags[foo]}}", "text", tags: tags));

        tags = new TagCollection { { "foo", "bar" } };
        Assert.Equal("text; Foo:bar", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$tags[foo]}}", "text", tags: tags));

        tags = new TagCollection { { "foo", ["bar", "baz"] } };
        Assert.Equal("text; Foo:[bar, baz]", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$tags[foo]}}", "text", tags: tags));

        Assert.Equal("text; Tags:", PromptUtils.RenderFactTemplate("{{$content}}; Tags:{{$tags}}", "text"));

        tags = new TagCollection { { "foo", ["bar", "baz"] } };
        Assert.Equal("text; Tags=foo:[bar, baz]", PromptUtils.RenderFactTemplate("{{$content}}; Tags={{$tags}}", "text", tags: tags));

        tags = new TagCollection { { "foo", ["bar", "baz"] }, { "car", ["red", "pink"] } };
        Assert.Equal("text; Tags=foo:[bar, baz];car:[red, pink]", PromptUtils.RenderFactTemplate("{{$content}}; Tags={{$tags}}", "text", tags: tags));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    public void ItRendersFactTemplatesWithMetadata()
    {
        Assert.Equal("text; Foo:-", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$meta[foo]}}", "text"));
        Assert.Equal("text; Foo:-", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$meta[foo]}}", "text", metadata: []));
        Assert.Equal("text; Foo:bar", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$meta[foo]}}", "text", metadata: new Dictionary<string, object> { { "foo", "bar" } }));
        Assert.Equal("text; Foo:bar", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$meta[foo]}}", "text", metadata: new Dictionary<string, object> { { "foo", "bar" }, { "car", "red" } }));
        Assert.Equal("text; Foo:bar; Car:red", PromptUtils.RenderFactTemplate("{{$content}}; Foo:{{$meta[foo]}}; Car:{{$meta[car]}}", "text", metadata: new Dictionary<string, object> { { "foo", "bar" }, { "car", "red" } }));
    }
}
