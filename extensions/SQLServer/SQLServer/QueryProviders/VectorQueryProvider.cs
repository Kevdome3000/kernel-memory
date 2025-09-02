// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Data.SqlClient;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.MemoryDb.SQLServer.QueryProviders;

internal sealed class VectorQueryProvider : ISqlServerQueryProvider
{
    private readonly SqlServerConfig _config;


    public VectorQueryProvider(SqlServerConfig config)
    {
        _config = config;
    }


    /// <inheritdoc/>
    public string PrepareCreateIndexQuery(int sqlServerVersion, string index, int vectorSize)
    {
        var sql = $"""
                   BEGIN TRANSACTION;

                       INSERT INTO {GetFullTableName(_config.MemoryCollectionTableName)}([id])
                           VALUES (@index);

                       IF OBJECT_ID(N'{GetFullTableName($"{_config.TagsTableName}_{index}")}', N'U') IS NULL
                           CREATE TABLE {GetFullTableName($"{_config.TagsTableName}_{index}")}
                           (
                               [memory_id] UNIQUEIDENTIFIER NOT NULL,
                               [name] NVARCHAR(256)  NOT NULL,
                               [value] NVARCHAR(256) NOT NULL,
                               FOREIGN KEY ([memory_id]) REFERENCES {GetFullTableName(_config.MemoryTableName)}([id])
                           );

                   COMMIT;
                   """;

        return sql;
    }


    /// <inheritdoc/>
    public string PrepareDeleteRecordQuery(string index)
    {
        var sql = $"""
                   BEGIN TRANSACTION;

                       DELETE [tags]
                           FROM {GetFullTableName($"{_config.TagsTableName}_{index}")} [tags]
                           INNER JOIN {GetFullTableName(_config.MemoryTableName)} ON [tags].[memory_id] = {GetFullTableName(_config.MemoryTableName)}.[id]
                           WHERE
                               {GetFullTableName(_config.MemoryTableName)}.[collection] = @index
                               AND {GetFullTableName(_config.MemoryTableName)}.[key]=@key;

                       DELETE FROM {GetFullTableName(_config.MemoryTableName)}
                           WHERE [collection] = @index AND [key]=@key;

                   COMMIT;
                   """;

        return sql;
    }


    /// <inheritdoc/>
    public string PrepareDeleteIndexQuery(string index)
    {
        var sql = $"""
                   BEGIN TRANSACTION;

                       DROP TABLE {GetFullTableName($"{_config.TagsTableName}_{index}")};

                       DELETE FROM {GetFullTableName(_config.MemoryCollectionTableName)}
                              WHERE [id] = @index;

                   COMMIT;
                   """;

        return sql;
    }


    /// <inheritdoc/>
    public string PrepareGetIndexesQuery()
    {
        var sql = $"SELECT [id] FROM {GetFullTableName(_config.MemoryCollectionTableName)}";
        return sql;
    }


    /// <inheritdoc/>
    public string PrepareGetRecordsListQuery(
        string index,
        ICollection<MemoryFilter>? filters,
        bool withEmbeddings,
        SqlParameterCollection parameters)
    {
        var queryColumns = "[key], [payload], [tags]";

        if (withEmbeddings) { queryColumns += ", CAST([embedding] AS NVARCHAR(MAX)) AS [embedding]"; }

        var sql = $"""
                   WITH [filters] AS
                   (
                       SELECT
                           cast([filters].[key] AS NVARCHAR(256)) COLLATE SQL_Latin1_General_CP1_CI_AS AS [name],
                           cast([filters].[value] AS NVARCHAR(256)) COLLATE SQL_Latin1_General_CP1_CI_AS AS [value]
                       FROM openjson(@filters) [filters]
                   )
                   SELECT TOP (@limit)
                       {queryColumns}
                   FROM
                       {GetFullTableName(_config.MemoryTableName)}
                   WHERE
                       {GetFullTableName(_config.MemoryTableName)}.[collection] = @index
                       {GenerateFilters(index, parameters, filters)};
                   """;

        return sql;
    }


    /// <inheritdoc/>
    public string PrepareGetSimilarRecordsListQuery(
        string index,
        ICollection<MemoryFilter>? filters,
        bool withEmbedding,
        SqlParameterCollection parameters)
    {
        var queryColumns = $"{GetFullTableName(_config.MemoryTableName)}.[id]," + $"{GetFullTableName(_config.MemoryTableName)}.[key]," + $"{GetFullTableName(_config.MemoryTableName)}.[payload]," + $"{GetFullTableName(_config.MemoryTableName)}.[tags]";

        if (withEmbedding)
        {
            queryColumns += $"," + $"CAST({GetFullTableName(_config.MemoryTableName)}.[embedding] AS NVARCHAR(MAX)) AS [embedding]";
        }

        var generatedFilters = GenerateFilters(index, parameters, filters);

        var sql = $"""
                   SELECT TOP (@limit)
                       {queryColumns},
                       VECTOR_DISTANCE('cosine', CAST(@vector AS VECTOR({_config.VectorSize})), Embedding) AS [distance]
                   FROM
                       {GetFullTableName(_config.MemoryTableName)}
                   WHERE
                       VECTOR_DISTANCE('cosine', CAST(@vector AS VECTOR({_config.VectorSize})), Embedding) <= @max_distance
                       {generatedFilters}
                   ORDER BY [distance] ASC
                   """;

        return sql;
    }


    /// <inheritdoc/>
    public string PrepareUpsertRecordsBatchQuery(string index)
    {
        var sql = $"""
                   BEGIN TRANSACTION;

                       MERGE INTO {GetFullTableName(_config.MemoryTableName)}
                           USING (SELECT @key) as [src]([key])
                           ON {GetFullTableName(_config.MemoryTableName)}.[key] = [src].[key]
                           WHEN MATCHED THEN
                               UPDATE SET payload=@payload, embedding=CAST(@embedding AS VECTOR({_config.VectorSize})), tags=@tags
                           WHEN NOT MATCHED THEN
                               INSERT ([key], [collection], [payload], [tags], [embedding])
                               VALUES (@key, @index, @payload, @tags, CAST(@embedding AS VECTOR({_config.VectorSize})));

                       DELETE FROM [tgt]
                           FROM  {GetFullTableName($"{_config.TagsTableName}_{index}")} AS [tgt]
                           INNER JOIN {GetFullTableName(_config.MemoryTableName)} ON [tgt].[memory_id] = {GetFullTableName(_config.MemoryTableName)}.[id]
                           WHERE {GetFullTableName(_config.MemoryTableName)}.[key] = @key
                                 AND {GetFullTableName(_config.MemoryTableName)}.[collection] = @index;

                       MERGE {GetFullTableName($"{_config.TagsTableName}_{index}")} AS [tgt]
                           USING (
                               SELECT
                                   {GetFullTableName(_config.MemoryTableName)}.[id],
                                   cast([tags].[key] AS NVARCHAR(MAX)) COLLATE SQL_Latin1_General_CP1_CI_AS AS [tag_name],
                                   [tag_value].[value] AS [value]
                               FROM {GetFullTableName(_config.MemoryTableName)}
                               CROSS APPLY openjson(@tags) [tags]
                               CROSS APPLY openjson(cast([tags].[value] AS NVARCHAR(MAX)) COLLATE SQL_Latin1_General_CP1_CI_AS) [tag_value]
                               WHERE {GetFullTableName(_config.MemoryTableName)}.[key] = @key
                                   AND {GetFullTableName(_config.MemoryTableName)}.[collection] = @index
                           ) AS [src]
                           ON [tgt].[memory_id] = [src].[id] AND [tgt].[name] = [src].[tag_name]
                           WHEN MATCHED THEN
                               UPDATE SET [tgt].[value] = [src].[value]
                           WHEN NOT MATCHED THEN
                               INSERT ([memory_id], [name], [value])
                               VALUES ([src].[id],
                                       [src].[tag_name],
                                       [src].[value]);

                   COMMIT;
                   """;

        return sql;
    }


    /// <inheritdoc/>
    public string PrepareCreateAllSupportingTablesQuery()
    {
        var sql = $"""
                   IF NOT EXISTS (SELECT  *
                                   FROM   sys.schemas
                                   WHERE  name = N'{_config.Schema}' )
                   EXEC('CREATE SCHEMA [{_config.Schema}]');

                   IF OBJECT_ID(N'{GetFullTableName(_config.MemoryCollectionTableName)}', N'U') IS NULL
                       CREATE TABLE {GetFullTableName(_config.MemoryCollectionTableName)}
                       (   [id] NVARCHAR(256) NOT NULL,
                           PRIMARY KEY ([id])
                       );

                   IF OBJECT_ID(N'{GetFullTableName(_config.MemoryTableName)}', N'U') IS NULL
                       CREATE TABLE {GetFullTableName(_config.MemoryTableName)}
                       (   [id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
                           [key] NVARCHAR(256)  NOT NULL,
                           [collection] NVARCHAR(256) NOT NULL,
                           [payload] NVARCHAR(MAX),
                           [tags] NVARCHAR(MAX),
                           [embedding] VECTOR({_config.VectorSize}),
                           PRIMARY KEY ([id]),
                           FOREIGN KEY ([collection]) REFERENCES {GetFullTableName(_config.MemoryCollectionTableName)}([id]) ON DELETE CASCADE,
                           CONSTRAINT UK_{_config.MemoryTableName} UNIQUE([collection], [key])
                       );
                   """;

        return sql;
    }


    private string GetFullTableName(string tableName)
    {
        return Utils.GetFullTableName(_config, tableName);
    }


    private string GenerateFilters(
        string index,
        SqlParameterCollection parameters,
        ICollection<MemoryFilter>? filters)
    {
        return Utils.GenerateFilters(_config,
            index,
            parameters,
            filters);
    }
}
