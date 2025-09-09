// Copyright (c) Microsoft.All rights reserved.

using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Models;
using Microsoft.KM.TestHelpers;

namespace Microsoft.Neo4j.UnitTests;

/// <summary>
/// Comprehensive tests for complex filter logic including OR-of-ANDs scenarios,
/// multi-value tag support, and edge cases in filter translation.
/// </summary>
public class ComplexFilterLogicTests : BaseUnitTestCase
{
    public ComplexFilterLogicTests(ITestOutputHelper output) : base(output)
    {
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesComplexOrOfAndsFilterLogic()
    {
        // Arrange - Create complex filter scenario: (user=admin AND type=news) OR (user=editor AND category=tech)
        var filter1 = new MemoryFilter();
        filter1.Add("user", "admin");
        filter1.Add("type", "news");

        var filter2 = new MemoryFilter();
        filter2.Add("user", "editor");
        filter2.Add("category", "tech");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains(" OR ", whereClause);
        Assert.Contains(" AND ", whereClause);

        // Should have 4 parameters (2 for each filter)
        Assert.Equal(4, parameters.Count);

        // Verify OR structure
        Assert.Contains("(", whereClause);
        Assert.Contains(")", whereClause);

        // Verify parameter naming
        Assert.True(parameters.ContainsKey("filterParam0"));
        Assert.True(parameters.ContainsKey("filterParam1"));
        Assert.True(parameters.ContainsKey("filterParam2"));
        Assert.True(parameters.ContainsKey("filterParam3"));

        // Verify parameter values
        Assert.Contains("admin", (List<string>)parameters["filterParam0"]);
        Assert.Contains("news", (List<string>)parameters["filterParam1"]);
        Assert.Contains("editor", (List<string>)parameters["filterParam2"]);
        Assert.Contains("tech", (List<string>)parameters["filterParam3"]);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesMultiValueTagsWithAnyMatching()
    {
        // Arrange - Single filter with multiple values for same key (ANY matching)
        var filter = new MemoryFilter();
        filter.Add("user", "admin");
        filter.Add("user", "editor");
        filter.Add("user", "owner");
        filter.Add("status", "active");
        filter.Add("status", "pending");

        var filters = new List<MemoryFilter> { filter };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains(" AND ", whereClause);
        Assert.Equal(2, parameters.Count); // One for user values, one for status values

        // Verify ANY pattern in Cypher
        Assert.Contains("ANY(v IN n.tags['user'] WHERE v IN $filterParam0)", whereClause);
        Assert.Contains("ANY(v IN n.tags['status'] WHERE v IN $filterParam1)", whereClause);

        // Verify parameter values contain all multi-values
        var userValues = (List<string>)parameters["filterParam0"];
        Assert.Equal(3, userValues.Count);
        Assert.Contains("admin", userValues);
        Assert.Contains("editor", userValues);
        Assert.Contains("owner", userValues);

        var statusValues = (List<string>)parameters["filterParam1"];
        Assert.Equal(2, statusValues.Count);
        Assert.Contains("active", statusValues);
        Assert.Contains("pending", statusValues);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesComplexThreeWayOrLogic()
    {
        // Arrange - Three filters with different combinations
        var filter1 = new MemoryFilter();
        filter1.Add("department", "engineering");
        filter1.Add("level", "senior");

        var filter2 = new MemoryFilter();
        filter2.Add("department", "marketing");
        filter2.Add("level", "junior");

        var filter3 = new MemoryFilter();
        filter3.Add("department", "sales");
        filter3.Add("role", "manager");

        var filters = new List<MemoryFilter> { filter1, filter2, filter3 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);

        // Should have two OR operators for three filters
        var orCount = whereClause.Split(" OR ").Length - 1;
        Assert.Equal(2, orCount);

        // Should have 6 parameters (2 for each filter)
        Assert.Equal(6, parameters.Count);

        // Verify all departments are represented
        var allValues = parameters.Values.SelectMany(v => (List<string>)v).ToList();
        Assert.Contains("engineering", allValues);
        Assert.Contains("marketing", allValues);
        Assert.Contains("sales", allValues);
        Assert.Contains("senior", allValues);
        Assert.Contains("junior", allValues);
        Assert.Contains("manager", allValues);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesMixedSingleAndMultiValueFilters()
    {
        // Arrange - Mix of single and multi-value filters
        var filter1 = new MemoryFilter();
        filter1.Add("category", "tech");
        filter1.Add("priority", "high");
        filter1.Add("priority", "critical");

        var filter2 = new MemoryFilter();
        filter2.Add("category", "business");
        filter2.Add("status", "active");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains(" OR ", whereClause);
        Assert.Contains(" AND ", whereClause);
        Assert.Equal(4, parameters.Count);

        // Verify multi-value handling
        var priorityValues = parameters.Values
            .Cast<List<string>>()
            .FirstOrDefault(v => v.Contains("high"));
        Assert.NotNull(priorityValues);
        Assert.Equal(2, priorityValues.Count);
        Assert.Contains("high", priorityValues);
        Assert.Contains("critical", priorityValues);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesReservedTagsInComplexFilters()
    {
        // Arrange - Include reserved document ID tag in complex filter
        var filter1 = new MemoryFilter();
        filter1.Add(Constants.ReservedDocumentIdTag, "doc1");
        filter1.Add("type", "important");

        var filter2 = new MemoryFilter();
        filter2.Add(Constants.ReservedDocumentIdTag, "doc2");
        filter2.Add("category", "urgent");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains(" OR ", whereClause);
        Assert.Equal(4, parameters.Count);

        // Verify reserved tag is treated like any other tag
        Assert.Contains($"n.tags['{Constants.ReservedDocumentIdTag}']", whereClause);

        var allValues = parameters.Values.SelectMany(v => (List<string>)v).ToList();
        Assert.Contains("doc1", allValues);
        Assert.Contains("doc2", allValues);
        Assert.Contains("important", allValues);
        Assert.Contains("urgent", allValues);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesLargeNumberOfFilters()
    {
        // Arrange - Create many filters to test scalability
        var filters = new List<MemoryFilter>();

        for (int i = 0; i < 10; i++)
        {
            var filter = new MemoryFilter();
            filter.Add("batch", $"batch_{i}");
            filter.Add("index", i.ToString());
            filters.Add(filter);
        }

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);

        // Should have 9 OR operators for 10 filters
        var orCount = whereClause.Split(" OR ").Length - 1;
        Assert.Equal(9, orCount);

        // Should have 20 parameters (2 for each filter)
        Assert.Equal(20, parameters.Count);

        // Verify parameter naming is sequential
        for (int i = 0; i < 20; i++)
        {
            Assert.True(parameters.ContainsKey($"filterParam{i}"));
        }
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesSpecialCharactersInFilterValues()
    {
        // Arrange - Filter values with special characters
        var filter = new MemoryFilter();
        filter.Add("path", "/home/user/documents");
        filter.Add("query", "SELECT * FROM table WHERE id = 'test'");
        filter.Add("regex", @"^\d{3}-\d{2}-\d{4}$");
        filter.Add("unicode", "测试数据");

        var filters = new List<MemoryFilter> { filter };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Equal(4, parameters.Count);

        // Verify special characters are preserved in parameters
        var allValues = parameters.Values.SelectMany(v => (List<string>)v).ToList();
        Assert.Contains("/home/user/documents", allValues);
        Assert.Contains("SELECT * FROM table WHERE id = 'test'", allValues);
        Assert.Contains(@"^\d{3}-\d{2}-\d{4}$", allValues);
        Assert.Contains("测试数据", allValues);

        // Verify Cypher structure is not broken by special characters
        Assert.Contains("ANY(v IN n.tags['path'] WHERE v IN $filterParam", whereClause);
        Assert.Contains("ANY(v IN n.tags['query'] WHERE v IN $filterParam", whereClause);
        Assert.Contains("ANY(v IN n.tags['regex'] WHERE v IN $filterParam", whereClause);
        Assert.Contains("ANY(v IN n.tags['unicode'] WHERE v IN $filterParam", whereClause);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesEmptyAndNullValuesInComplexFilters()
    {
        // Arrange - Mix of valid, null, and empty values
        var filter1 = new MemoryFilter();
        filter1.Add("valid", "value1");
        filter1.Add("valid", (string?)null); // Should be ignored
        filter1.Add("valid", "value2");

        var filter2 = new MemoryFilter();
        filter2.Add("another", "value3");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Equal(2, parameters.Count);

        // Verify null values are filtered out
        var validValues = (List<string>)parameters["filterParam0"];
        Assert.Equal(2, validValues.Count);
        Assert.Contains("value1", validValues);
        Assert.Contains("value2", validValues);
        Assert.DoesNotContain(null, validValues);

        var anotherValues = (List<string>)parameters["filterParam1"];
        Assert.Single(anotherValues);
        Assert.Contains("value3", anotherValues);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItHandlesCustomNodeAliasInComplexFilters()
    {
        // Arrange
        var filter1 = new MemoryFilter();
        filter1.Add("user", "admin");
        filter1.Add("role", "manager");

        var filter2 = new MemoryFilter();
        filter2.Add("user", "editor");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters, "customNode");

        // Assert
        Assert.Contains("WHERE", whereClause);
        Assert.Contains("customNode.tags['user']", whereClause);
        Assert.Contains("customNode.tags['role']", whereClause);
        Assert.DoesNotContain("n.tags", whereClause);
        Assert.Equal(3, parameters.Count);
    }


    [Fact]
    [Trait("Category", "UnitTest")]
    [Trait("Category", "Neo4j")]
    public void ItGeneratesCorrectCypherStructureForComplexScenario()
    {
        // Arrange - Real-world complex scenario
        var filter1 = new MemoryFilter();
        filter1.Add("department", "engineering");
        filter1.Add("level", "senior");
        filter1.Add("level", "principal");
        filter1.Add("skills", "neo4j");

        var filter2 = new MemoryFilter();
        filter2.Add("department", "data");
        filter2.Add("level", "senior");

        var filters = new List<MemoryFilter> { filter1, filter2 };

        // Act
        (var whereClause, var parameters) = Neo4jMemory.BuildWhereClause(filters);

        // Assert - Verify complete Cypher structure
        Assert.StartsWith(" WHERE ", whereClause);

        // Should contain proper parentheses for grouping
        var openParens = whereClause.Count(c => c == '(');
        var closeParens = whereClause.Count(c => c == ')');
        Assert.Equal(openParens, closeParens);
        Assert.True(openParens >= 2); // At least one for each OR group

        // Should contain proper AND/OR structure
        Assert.Contains(" AND ", whereClause);
        Assert.Contains(" OR ", whereClause);

        // Verify parameter structure
        Assert.Equal(5, parameters.Count); // department, level (multi-value), skills, department, level

        // Verify Cypher injection prevention (all values are parameterized)
        Assert.DoesNotContain("engineering", whereClause);
        Assert.DoesNotContain("senior", whereClause);
        Assert.DoesNotContain("neo4j", whereClause);
        Assert.DoesNotContain("data", whereClause);
    }
}
