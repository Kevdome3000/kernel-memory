// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.UnitTests;

public class NamingHelpersTests
{
    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void NormalizeIndexNameHandlesBasicCases()
    {
        // Act & Assert
        Assert.Equal("myindex", Neo4jMemory.NormalizeIndexName("MyIndex"));
        Assert.Equal("simple", Neo4jMemory.NormalizeIndexName("simple"));
        Assert.Equal("test_index", Neo4jMemory.NormalizeIndexName("test_index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void NormalizeIndexNameReplacesInvalidCharacters()
    {
        // Act & Assert
        Assert.Equal("my_index", Neo4jMemory.NormalizeIndexName("my index"));
        Assert.Equal("my_index", Neo4jMemory.NormalizeIndexName("my\\index"));
        Assert.Equal("my_index", Neo4jMemory.NormalizeIndexName("my/index"));
        Assert.Equal("my_index", Neo4jMemory.NormalizeIndexName("my.index"));
        Assert.Equal("my_index", Neo4jMemory.NormalizeIndexName("my_index"));
        Assert.Equal("my_index", Neo4jMemory.NormalizeIndexName("my:index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void NormalizeIndexNameHandlesMultipleInvalidCharacters()
    {
        // Act & Assert
        Assert.Equal("my_complex_index_name", Neo4jMemory.NormalizeIndexName("my complex\\index/name"));
        Assert.Equal("test_with_many_chars", Neo4jMemory.NormalizeIndexName("test with.many_chars"));
        Assert.Equal("mixed_separators_and_underscores_here", Neo4jMemory.NormalizeIndexName("mixed\\separators/and.underscores_here"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void NormalizeIndexNameHandlesEdgeCases()
    {
        // Act & Assert
        Assert.Equal("leading_and_trailing", Neo4jMemory.NormalizeIndexName("  leading and trailing  "));
        Assert.Equal("multiple_spaces", Neo4jMemory.NormalizeIndexName("multiple   spaces"));
        Assert.Equal("consecutive_separators", Neo4jMemory.NormalizeIndexName("consecutive___separators"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void NormalizeIndexNameThrowsOnNullOrEmpty()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Neo4jMemory.NormalizeIndexName(null!));
        Assert.Throws<ArgumentNullException>(() => Neo4jMemory.NormalizeIndexName(""));
        Assert.Throws<ArgumentNullException>(() => Neo4jMemory.NormalizeIndexName("   "));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void PropertyKeyForIndexGeneratesConsistentKeys()
    {
        // Act & Assert
        Assert.Equal("vec_myindex", Neo4jMemory.PropertyKeyForIndex("MyIndex"));
        Assert.Equal("vec_simple", Neo4jMemory.PropertyKeyForIndex("simple"));
        Assert.Equal("vec_test_index", Neo4jMemory.PropertyKeyForIndex("Test_Index"));
        Assert.Equal("vec_complex_name", Neo4jMemory.PropertyKeyForIndex("Complex_Name"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void PropertyKeyForIndexIsDeterministic()
    {
        // Arrange
        const string indexName = "TestIndex";

        // Act
        string result1 = Neo4jMemory.PropertyKeyForIndex(indexName);
        string result2 = Neo4jMemory.PropertyKeyForIndex(indexName);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal("vec_testindex", result1);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void LabelForIndexGeneratesCorrectLabels()
    {
        // Arrange
        var config = new Neo4jConfig();
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("MYINDEX", memory.LabelForIndex("MyIndex"));
        Assert.Equal("SIMPLE", memory.LabelForIndex("simple"));
        Assert.Equal("TEST_INDEX", memory.LabelForIndex("Test_Index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void LabelForIndexHandlesPrefix()
    {
        // Arrange
        var config = new Neo4jConfig { LabelPrefix = "KM_" };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("KM_MYINDEX", memory.LabelForIndex("MyIndex"));
        Assert.Equal("KM_SIMPLE", memory.LabelForIndex("simple"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void LabelForIndexHandlesEmptyPrefix()
    {
        // Arrange
        var config = new Neo4jConfig { LabelPrefix = "" };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("MYINDEX", memory.LabelForIndex("MyIndex"));
        Assert.Equal("SIMPLE", memory.LabelForIndex("simple"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void LabelForIndexHandlesNullPrefix()
    {
        // Arrange
        var config = new Neo4jConfig { LabelPrefix = null };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("MYINDEX", memory.LabelForIndex("MyIndex"));
        Assert.Equal("SIMPLE", memory.LabelForIndex("simple"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ApplyIndexNamePrefixHandlesNoPrefix()
    {
        // Arrange
        var config = new Neo4jConfig();
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("myindex", memory.ApplyIndexNamePrefix("myindex"));
        Assert.Equal("test_index", memory.ApplyIndexNamePrefix("test_index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ApplyIndexNamePrefixHandlesPrefix()
    {
        // Arrange
        var config = new Neo4jConfig { IndexNamePrefix = "km_" };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("km_myindex", memory.ApplyIndexNamePrefix("myindex"));
        Assert.Equal("km_test_index", memory.ApplyIndexNamePrefix("test_index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ApplyIndexNamePrefixHandlesEmptyPrefix()
    {
        // Arrange
        var config = new Neo4jConfig { IndexNamePrefix = "" };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("myindex", memory.ApplyIndexNamePrefix("myindex"));
        Assert.Equal("test_index", memory.ApplyIndexNamePrefix("test_index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ApplyIndexNamePrefixHandlesNullPrefix()
    {
        // Arrange
        var config = new Neo4jConfig { IndexNamePrefix = null };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());

        // Act & Assert
        Assert.Equal("myindex", memory.ApplyIndexNamePrefix("myindex"));
        Assert.Equal("test_index", memory.ApplyIndexNamePrefix("test_index"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void NamingHelpersWorkTogether()
    {
        // Arrange
        var config = new Neo4jConfig
        {
            LabelPrefix = "KM_",
            IndexNamePrefix = "km_"
        };
        var memory = new Neo4jMemory(config, new FakeEmbeddingGenerator());
        const string originalIndex = "My Complex/Index Name";

        // Act
        string normalized = Neo4jMemory.NormalizeIndexName(originalIndex);
        string withPrefix = memory.ApplyIndexNamePrefix(normalized);
        string label = memory.LabelForIndex(normalized);
        string propertyKey = Neo4jMemory.PropertyKeyForIndex(normalized);

        // Assert
        Assert.Equal("my_complex_index_name", normalized);
        Assert.Equal("km_my_complex_index_name", withPrefix);
        Assert.Equal("KM_MY_COMPLEX_INDEX_NAME", label);
        Assert.Equal("vec_my_complex_index_name", propertyKey);
    }
}
