// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;

namespace Microsoft.Neo4j.UnitTests;

public class Neo4jConfigTests
{
    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHasCorrectDefaultValues()
    {
        // Arrange & Act
        var config = new Neo4jConfig();

        // Assert
        Assert.Equal("neo4j://localhost:7687", config.Uri);
        Assert.Equal("neo4j", config.Username);
        Assert.Equal(string.Empty, config.Password);
        Assert.Null(config.IndexNamePrefix);
        Assert.Null(config.LabelPrefix);
        Assert.False(config.StrictVectorSizeValidation);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItAllowsSettingAllProperties()
    {
        // Arrange
        var config = new Neo4jConfig();

        // Act
        config.Uri = "neo4j://custom-host:7687";
        config.Username = "custom-user";
        config.Password = "custom-password";
        config.IndexNamePrefix = "km_";
        config.LabelPrefix = "KM_";
        config.StrictVectorSizeValidation = true;

        // Assert
        Assert.Equal("neo4j://custom-host:7687", config.Uri);
        Assert.Equal("custom-user", config.Username);
        Assert.Equal("custom-password", config.Password);
        Assert.Equal("km_", config.IndexNamePrefix);
        Assert.Equal("KM_", config.LabelPrefix);
        Assert.True(config.StrictVectorSizeValidation);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesNullAndEmptyPrefixes()
    {
        // Arrange
        var config = new Neo4jConfig();

        // Act & Assert - null prefixes
        config.IndexNamePrefix = null;
        config.LabelPrefix = null;
        Assert.Null(config.IndexNamePrefix);
        Assert.Null(config.LabelPrefix);

        // Act & Assert - empty prefixes
        config.IndexNamePrefix = string.Empty;
        config.LabelPrefix = string.Empty;
        Assert.Equal(string.Empty, config.IndexNamePrefix);
        Assert.Equal(string.Empty, config.LabelPrefix);

        // Act & Assert - whitespace prefixes
        config.IndexNamePrefix = "   ";
        config.LabelPrefix = "   ";
        Assert.Equal("   ", config.IndexNamePrefix);
        Assert.Equal("   ", config.LabelPrefix);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItSupportsVariousUriFormats()
    {
        // Arrange
        var config = new Neo4jConfig();

        // Act & Assert - different URI schemes
        config.Uri = "bolt://localhost:7687";
        Assert.Equal("bolt://localhost:7687", config.Uri);

        config.Uri = "neo4j+s://secure-host:7687";
        Assert.Equal("neo4j+s://secure-host:7687", config.Uri);

        config.Uri = "bolt+s://secure-host:7687";
        Assert.Equal("bolt+s://secure-host:7687", config.Uri);

        config.Uri = "neo4j://cluster.example.com:7687";
        Assert.Equal("neo4j://cluster.example.com:7687", config.Uri);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesPasswordSecurity()
    {
        // Arrange
        var config = new Neo4jConfig();

        // Act
        config.Password = "sensitive-password-123!@#";

        // Assert
        Assert.Equal("sensitive-password-123!@#", config.Password);

        // Act - empty password
        config.Password = string.Empty;
        Assert.Equal(string.Empty, config.Password);
    }
}
