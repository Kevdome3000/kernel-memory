// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;

namespace Microsoft.Neo4j.UnitTests;

public class TagConversionTests
{
    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesNullNeo4jTags()
    {
        // Arrange & Act
        TagCollection result = Neo4jMemory.ConvertTagsFromNeo4j(null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesEmptyNeo4jTags()
    {
        // Arrange
        List<string> neo4jTags = [];

        // Act
        TagCollection result = Neo4jMemory.ConvertTagsFromNeo4j(neo4jTags);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItConvertsSingleKeyWithSingleValue()
    {
        // Arrange
        Dictionary<string, List<string>> neo4jTags = new()
        {
            { "user", ["admin"] }
        };

        List<string> flattenedTags = neo4jTags
            .SelectMany(tag => tag.Value.Select(value => $"{tag.Key}{Constants.ReservedEqualsChar}{value}"))
            .ToList();

        // Act
        TagCollection result = Neo4jMemory.ConvertTagsFromNeo4j(flattenedTags);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.True(result.ContainsKey("user"));
        Assert.Single(result["user"]);
        Assert.Equal("admin", result["user"][0]);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItConvertsSingleKeyWithMultipleValues()
    {
        // Arrange
        Dictionary<string, List<string>> neo4jTags = new()
        {
            { "user", ["admin", "owner", "editor"] }
        };

        List<string> flattenedTags = neo4jTags
            .SelectMany(tag => tag.Value.Select(value => $"{tag.Key}{Constants.ReservedEqualsChar}{value}"))
            .ToList();

        // Act
        TagCollection result = Neo4jMemory.ConvertTagsFromNeo4j(flattenedTags);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.True(result.ContainsKey("user"));
        Assert.Equal(3, result["user"].Count);
        Assert.Contains("admin", result["user"]);
        Assert.Contains("owner", result["user"]);
        Assert.Contains("editor", result["user"]);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItConvertsMultipleKeysWithMultipleValues()
    {
        // Arrange
        Dictionary<string, List<string>> neo4jTags = new()
        {
            { "user", ["admin", "owner"] },
            { "type", ["news", "article"] },
            { "category", ["tech"] }
        };

        List<string> flattenedTags = neo4jTags
            .SelectMany(tag => tag.Value.Select(value => $"{tag.Key}{Constants.ReservedEqualsChar}{value}"))
            .ToList();

        // Act
        TagCollection result = Neo4jMemory.ConvertTagsFromNeo4j(flattenedTags);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);

        Assert.True(result.ContainsKey("user"));
        Assert.Equal(2, result["user"].Count);
        Assert.Contains("admin", result["user"]);
        Assert.Contains("owner", result["user"]);

        Assert.True(result.ContainsKey("type"));
        Assert.Equal(2, result["type"].Count);
        Assert.Contains("news", result["type"]);
        Assert.Contains("article", result["type"]);

        Assert.True(result.ContainsKey("category"));
        Assert.Single(result["category"]);
        Assert.Equal("tech", result["category"][0]);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesEmptyValueLists()
    {
        // Arrange
        Dictionary<string, List<string>> neo4jTags = new()
        {
            { "user", ["admin"] },
            { "empty", [] },
            { "type", ["news"] }
        };

        List<string> flattenedTags = neo4jTags
            .Where(tag => tag.Value.Count > 0) // Filter out empty lists
            .SelectMany(tag => tag.Value.Select(value => $"{tag.Key}{Constants.ReservedEqualsChar}{value}"))
            .ToList();

        // Act
        TagCollection result = Neo4jMemory.ConvertTagsFromNeo4j(flattenedTags);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // empty key should not be included
        Assert.True(result.ContainsKey("user"));
        Assert.True(result.ContainsKey("type"));
        Assert.False(result.ContainsKey("empty"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesNullAndEmptyFilters()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");

        // Act & Assert - null filters
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, null));

        // Act & Assert - empty filters
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, new List<MemoryFilter>()));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesSingleFilterMatch()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        MemoryFilter filter = new();
        filter.Add("user", "admin");
        List<MemoryFilter> filters = [filter];

        // Act & Assert
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesSingleFilterNoMatch()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        MemoryFilter filter = new();
        filter.Add("user", "owner");
        List<MemoryFilter> filters = [filter];

        // Act & Assert
        Assert.False(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMultipleKeyAndLogic()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        MemoryFilter filter = new();
        filter.Add("user", "admin");
        filter.Add("type", "news");
        List<MemoryFilter> filters = [filter];

        // Act & Assert - both conditions match
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));

        // Arrange - one condition doesn't match
        MemoryFilter filter2 = new();
        filter2.Add("user", "admin");
        filter2.Add("type", "article");
        List<MemoryFilter> filters2 = [filter2];

        // Act & Assert - should fail because of AND logic
        Assert.False(Neo4jMemory.TagsMatchFilters(tags, filters2));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMultipleFiltersOrLogic()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        MemoryFilter filter1 = new();
        filter1.Add("user", "owner"); // doesn't match

        MemoryFilter filter2 = new();
        filter2.Add("user", "admin"); // matches

        List<MemoryFilter> filters = [filter1, filter2];

        // Act & Assert - should pass because of OR logic
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMultipleValuesInTags()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");
        tags.Add("user", "owner");
        tags.Add("type", "news");

        MemoryFilter filter = new();
        filter.Add("user", "owner");
        List<MemoryFilter> filters = [filter];

        // Act & Assert - should match one of the user values
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMissingKeys()
    {
        // Arrange
        TagCollection tags = new();
        tags.Add("user", "admin");

        MemoryFilter filter = new();
        filter.Add("category", "tech"); // key doesn't exist in tags
        List<MemoryFilter> filters = [filter];

        // Act & Assert
        Assert.False(Neo4jMemory.TagsMatchFilters(tags, filters));
    }
}
