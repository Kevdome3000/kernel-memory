// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Neo4j;

namespace Microsoft.Neo4j.UnitTests;

public class TagConversionTests
{
    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesNullNeo4jTags()
    {
        // Arrange & Act
        var result = Neo4jMemory.ConvertTagsFromNeo4j(null);

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
        var neo4jTags = new Dictionary<string, List<string>>();

        // Act
        var result = Neo4jMemory.ConvertTagsFromNeo4j(neo4jTags);

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
        var neo4jTags = new Dictionary<string, List<string>>
        {
            { "user", new List<string> { "admin" } }
        };

        // Act
        var result = Neo4jMemory.ConvertTagsFromNeo4j(neo4jTags);

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
        var neo4jTags = new Dictionary<string, List<string>>
        {
            { "user", new List<string> { "admin", "owner", "editor" } }
        };

        // Act
        var result = Neo4jMemory.ConvertTagsFromNeo4j(neo4jTags);

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
        var neo4jTags = new Dictionary<string, List<string>>
        {
            { "user", new List<string> { "admin", "owner" } },
            { "type", new List<string> { "news", "article" } },
            { "category", new List<string> { "tech" } }
        };

        // Act
        var result = Neo4jMemory.ConvertTagsFromNeo4j(neo4jTags);

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
        var neo4jTags = new Dictionary<string, List<string>>
        {
            { "user", new List<string> { "admin" } },
            { "empty", new List<string>() },
            { "type", new List<string> { "news" } }
        };

        // Act
        var result = Neo4jMemory.ConvertTagsFromNeo4j(neo4jTags);

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
        var tags = new TagCollection();
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
        var tags = new TagCollection();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        var filters = new List<MemoryFilter> { filter };

        // Act & Assert
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesSingleFilterNoMatch()
    {
        // Arrange
        var tags = new TagCollection();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        var filter = new MemoryFilter();
        filter.Add("user", "owner");
        var filters = new List<MemoryFilter> { filter };

        // Act & Assert
        Assert.False(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMultipleKeyAndLogic()
    {
        // Arrange
        var tags = new TagCollection();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        filter.Add("type", "news");
        var filters = new List<MemoryFilter> { filter };

        // Act & Assert - both conditions match
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));

        // Arrange - one condition doesn't match
        var filter2 = new MemoryFilter();
        filter2.Add("user", "admin");
        filter2.Add("type", "article");
        var filters2 = new List<MemoryFilter> { filter2 };

        // Act & Assert - should fail because of AND logic
        Assert.False(Neo4jMemory.TagsMatchFilters(tags, filters2));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMultipleFiltersOrLogic()
    {
        // Arrange
        var tags = new TagCollection();
        tags.Add("user", "admin");
        tags.Add("type", "news");

        var filter1 = new MemoryFilter();
        filter1.Add("user", "owner"); // doesn't match

        var filter2 = new MemoryFilter();
        filter2.Add("user", "admin"); // matches

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act & Assert - should pass because of OR logic
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMultipleValuesInTags()
    {
        // Arrange
        var tags = new TagCollection();
        tags.Add("user", "admin");
        tags.Add("user", "owner");
        tags.Add("type", "news");

        var filter = new MemoryFilter();
        filter.Add("user", "owner");
        var filters = new List<MemoryFilter> { filter };

        // Act & Assert - should match one of the user values
        Assert.True(Neo4jMemory.TagsMatchFilters(tags, filters));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void TagsMatchFiltersHandlesMissingKeys()
    {
        // Arrange
        var tags = new TagCollection();
        tags.Add("user", "admin");

        var filter = new MemoryFilter();
        filter.Add("category", "tech"); // key doesn't exist in tags
        var filters = new List<MemoryFilter> { filter };

        // Act & Assert
        Assert.False(Neo4jMemory.TagsMatchFilters(tags, filters));
    }
}
