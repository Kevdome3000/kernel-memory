// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory.Models;
using Microsoft.KernelMemory.Neo4j;

namespace Microsoft.Neo4j.UnitTests;

public class FilterTranslationTests
{
    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesNullAndEmptyFilters()
    {
        // Arrange & Act - null filters
        var (whereClause1, parameters1) = Neo4jMemory.BuildWhereClause(null);

        // Assert
        Assert.Equal(string.Empty, whereClause1);
        Assert.Empty(parameters1);

        // Arrange & Act - empty filters
        var (whereClause2, parameters2) = Neo4jMemory.BuildWhereClause(new List<MemoryFilter>());

        // Assert
        Assert.Equal(string.Empty, whereClause2);
        Assert.Empty(parameters2);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesSingleFilterSingleKeyValue()
    {
        // Arrange
        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        var filters = new List<MemoryFilter> { filter };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Equal(" WHERE (ANY(v IN n.tags['user'] WHERE v IN $filterParam0))", whereClause);
        Assert.Single(parameters);
        Assert.True(parameters.ContainsKey("filterParam0"));
        var paramValues = (List<string>)parameters["filterParam0"];
        Assert.Single(paramValues);
        Assert.Equal("admin", paramValues[0]);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesSingleFilterMultipleValues()
    {
        // Arrange
        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        filter.Add("user", "owner");
        var filters = new List<MemoryFilter> { filter };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Equal(" WHERE (ANY(v IN n.tags['user'] WHERE v IN $filterParam0))", whereClause);
        Assert.Single(parameters);
        Assert.True(parameters.ContainsKey("filterParam0"));
        var paramValues = (List<string>)parameters["filterParam0"];
        Assert.Equal(2, paramValues.Count);
        Assert.Contains("admin", paramValues);
        Assert.Contains("owner", paramValues);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesSingleFilterMultipleKeys()
    {
        // Arrange
        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        filter.Add("type", "news");
        var filters = new List<MemoryFilter> { filter };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains("ANY(v IN n.tags['user'] WHERE v IN $filterParam0)", whereClause);
        Assert.Contains("ANY(v IN n.tags['type'] WHERE v IN $filterParam1)", whereClause);
        Assert.Contains(" AND ", whereClause);
        Assert.Equal(2, parameters.Count);
        Assert.True(parameters.ContainsKey("filterParam0"));
        Assert.True(parameters.ContainsKey("filterParam1"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesMultipleFiltersOrLogic()
    {
        // Arrange
        var filter1 = new MemoryFilter();
        filter1.Add("user", "admin");

        var filter2 = new MemoryFilter();
        filter2.Add("user", "owner");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains("ANY(v IN n.tags['user'] WHERE v IN $filterParam0)", whereClause);
        Assert.Contains("ANY(v IN n.tags['user'] WHERE v IN $filterParam1)", whereClause);
        Assert.Contains(" OR ", whereClause);
        Assert.Equal(2, parameters.Count);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesComplexOrOfAndsLogic()
    {
        // Arrange
        var filter1 = new MemoryFilter();
        filter1.Add("user", "admin");
        filter1.Add("type", "news");

        var filter2 = new MemoryFilter();
        filter2.Add("user", "owner");
        filter2.Add("category", "tech");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains(" OR ", whereClause);
        Assert.Contains(" AND ", whereClause);
        Assert.Equal(4, parameters.Count);

        // Verify parameter naming is sequential
        Assert.True(parameters.ContainsKey("filterParam0"));
        Assert.True(parameters.ContainsKey("filterParam1"));
        Assert.True(parameters.ContainsKey("filterParam2"));
        Assert.True(parameters.ContainsKey("filterParam3"));
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesCustomNodeAlias()
    {
        // Arrange
        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        var filters = new List<MemoryFilter> { filter };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters, "node");

        // Assert
        Assert.Contains("ANY(v IN node.tags['user'] WHERE v IN $filterParam0)", whereClause);
        Assert.Single(parameters);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItIgnoresEmptyFilters()
    {
        // Arrange
        var emptyFilter = new MemoryFilter();
        var validFilter = new MemoryFilter();
        validFilter.Add("user", "admin");
        var filters = new List<MemoryFilter> { emptyFilter, validFilter };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Equal(" WHERE (ANY(v IN n.tags['user'] WHERE v IN $filterParam0))", whereClause);
        Assert.Single(parameters);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesNullValuesInFilter()
    {
        // Arrange
        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        filter.Add("user", (string?)null); // null value should be ignored
        filter.Add("user", "owner");
        var filters = new List<MemoryFilter> { filter };

        // Act
        var (whereClause, parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Equal(" WHERE (ANY(v IN n.tags['user'] WHERE v IN $filterParam0))", whereClause);
        Assert.Single(parameters);
        var paramValues = (List<string>)parameters["filterParam0"];
        Assert.Equal(2, paramValues.Count);
        Assert.Contains("admin", paramValues);
        Assert.Contains("owner", paramValues);
        Assert.DoesNotContain(null, paramValues);
    }
}
