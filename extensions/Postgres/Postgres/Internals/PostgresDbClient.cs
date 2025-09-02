// Copyright (c) Microsoft.All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.MemoryStorage;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Microsoft.KernelMemory.Postgres.Internals;

/// <summary>
/// An implementation of a client for Postgres. This class is used to managing postgres database operations.
/// </summary>
internal sealed class PostgresDbClient : IDisposable, IAsyncDisposable
{
    // Dependencies
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger _log;


    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresDbClient"/> class.
    /// </summary>
    /// <param name="config">Configuration</param>
    /// <param name="loggerFactory">Application logger factory</param>
    public PostgresDbClient(PostgresConfig config, ILoggerFactory? loggerFactory = null)
    {
        config.Validate();
        _log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<PostgresDbClient>();

        NpgsqlDataSourceBuilder dataSourceBuilder = new(config.ConnectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();

        _dbNamePresent = config.ConnectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase);
        _schema = config.Schema;
        _tableNamePrefix = config.TableNamePrefix;

        _colId = config.Columns[PostgresConfig.ColumnId];
        _colEmbedding = config.Columns[PostgresConfig.ColumnEmbedding];
        _colTags = config.Columns[PostgresConfig.ColumnTags];
        _colContent = config.Columns[PostgresConfig.ColumnContent];
        _colPayload = config.Columns[PostgresConfig.ColumnPayload];

        PostgresSchema.ValidateSchemaName(_schema);
        PostgresSchema.ValidateTableNamePrefix(_tableNamePrefix);
        PostgresSchema.ValidateFieldName(_colId);
        PostgresSchema.ValidateFieldName(_colEmbedding);
        PostgresSchema.ValidateFieldName(_colTags);
        PostgresSchema.ValidateFieldName(_colContent);
        PostgresSchema.ValidateFieldName(_colPayload);

        _columnsListNoEmbeddings = $"{_colId},{_colTags},{_colContent},{_colPayload}";
        _columnsListWithEmbeddings = $"{_colId},{_colTags},{_colContent},{_colPayload},{_colEmbedding}";

        _createTableSql = string.Empty;

        if (config.CreateTableSql?.Count > 0)
        {
            _createTableSql = string.Join('\n', config.CreateTableSql).Trim();
        }
    }


    /// <summary>
    /// Check if a table exists.
    /// </summary>
    /// <param name="tableName">The name assigned to a table of entries</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    /// <returns>True if the table exists</returns>
    public async Task<bool> DoesTableExistAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        tableName = WithTableNamePrefix(tableName);
        _log.LogTrace("Checking if table {0} exists", tableName);

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // SQL reviewed
                    cmd.CommandText = @"
                        SELECT table_name
                        FROM information_schema.tables
                            WHERE table_schema = @schema
                                AND table_name = @table
                                AND table_type = 'BASE TABLE'
                        LIMIT 1
                    ";

                    cmd.Parameters.AddWithValue("@schema", _schema);
                    cmd.Parameters.AddWithValue("@table", tableName);
#pragma warning restore CA2100

                    _log.LogTrace("Schema: {0}, Table: {1}, SQL: {2}",
                        _schema,
                        tableName,
                        cmd.CommandText);

                    NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    await using (dataReader.ConfigureAwait(false))
                    {
                        if (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var name = dataReader.GetString(dataReader.GetOrdinal("table_name"));

                            return string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase);
                        }

                        _log.LogTrace("Table {0} does not exist", tableName);
                        return false;
                    }
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Create a table.
    /// </summary>
    /// <param name="tableName">The name assigned to a table of entries</param>
    /// <param name="vectorSize">Embedding vectors dimension</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    public async Task CreateTableAsync(
        string tableName,
        int vectorSize,
        CancellationToken cancellationToken = default)
    {
        var origInputTableName = tableName;
        tableName = WithSchemaAndTableNamePrefix(tableName);
        _log.LogTrace("Creating table: {0}", tableName);

        Npgsql.PostgresException? createErr = null;

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
                    var lockId = GenLockId(tableName);

#pragma warning disable CA2100 // SQL reviewed
                    if (!string.IsNullOrEmpty(_createTableSql))
                    {
                        cmd.CommandText = _createTableSql
                            .Replace(PostgresConfig.SqlPlaceholdersTableName, tableName, StringComparison.Ordinal)
                            .Replace(PostgresConfig.SqlPlaceholdersVectorSize, $"{vectorSize}", StringComparison.Ordinal)
                            .Replace(PostgresConfig.SqlPlaceholdersLockId, $"{lockId}", StringComparison.Ordinal);

                        _log.LogTrace("Creating table with custom SQL: {0}", cmd.CommandText);
                    }
                    else
                    {
                        cmd.CommandText = $@"
                            BEGIN;
                            SELECT pg_advisory_xact_lock({lockId});
                            CREATE TABLE IF NOT EXISTS {tableName} (
                                {_colId}        TEXT NOT NULL PRIMARY KEY,
                                {_colEmbedding} vector({vectorSize}),
                                {_colTags}      TEXT[] DEFAULT '{{}}'::TEXT[] NOT NULL,
                                {_colContent}   TEXT DEFAULT '' NOT NULL,
                                {_colPayload}   JSONB DEFAULT '{{}}'::JSONB NOT NULL
                            );
                            CREATE INDEX IF NOT EXISTS idx_tags ON {tableName} USING GIN({_colTags});
                            COMMIT;
                        ";
#pragma warning restore CA2100

                        _log.LogTrace("Creating table with default SQL: {0}", cmd.CommandText);
                    }

                    int result = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    _log.LogTrace("Table '{0}' creation result: {1}", tableName, result);
                }
            }
            catch (Npgsql.PostgresException e) when (IsVectorTypeDoesNotExistException(e))
            {
                _log.LogError(e, "Vector type not installed, check 'SELECT * FROM pg_extension'");
                throw;
            }
            catch (Npgsql.PostgresException e) when (e.SqlState == PgErrUniqueViolation)
            {
                createErr = e;
            }
            catch (Exception e)
            {
                _log.LogError(e,
                    "Table '{0}' creation error: {1}. Err: {2}. InnerEx: {3}",
                    tableName,
                    e,
                    e.Message,
                    e.InnerException);
                throw;
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        if (createErr != null)
        {
            // If the table exists, assume the table state is fine, logs some warnings, and continue
            if (await DoesTableExistAsync(origInputTableName, cancellationToken).ConfigureAwait(false))
            {
                // Check if the custom SQL contains the lock placeholder (assuming it's not commented out)
                bool missingLockStatement = !string.IsNullOrEmpty(_createTableSql)
                    && !_createTableSql.Contains(PostgresConfig.SqlPlaceholdersLockId, StringComparison.Ordinal);

                if (missingLockStatement)
                {
                    _log.LogWarning(
                        "Concurrency error: {0}; {1}; {2}. Add '{3}' to the custom SQL statement used to create tables. The table exists so the application will continue",
                        createErr.SqlState,
                        createErr.Message,
                        createErr.Detail,
                        PostgresConfig.SqlPlaceholdersLockId);
                }
                else
                {
                    _log.LogWarning("Postgres error while creating table: {0}; {1}; {2}. The table exists so the application will continue",
                        createErr.SqlState,
                        createErr.Message,
                        createErr.Detail);
                }
            }
            else
            {
                // But if the table doesn't exist, throw
                _log.LogError(createErr, "Table creation failed: {0}", tableName);
                throw createErr;
            }
        }
    }


    /// <summary>
    /// Get all tables
    /// </summary>
    /// <param name="cancellationToken">Async task cancellation token</param>
    /// <returns>A group of tables</returns>
    public async IAsyncEnumerable<string> GetTablesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandText = @"SELECT table_name FROM information_schema.tables
                                WHERE table_schema = @schema AND table_type = 'BASE TABLE';";
                    cmd.Parameters.AddWithValue("@schema", _schema);

                    _log.LogTrace("Fetching list of tables. SQL: {0}. Schema: {1}", cmd.CommandText, _schema);

                    NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                    await using (dataReader.ConfigureAwait(false))
                    {
                        while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var tableNameWithPrefix = dataReader.GetString(dataReader.GetOrdinal("table_name"));

                            if (tableNameWithPrefix.StartsWith(_tableNamePrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                yield return tableNameWithPrefix.Remove(0, _tableNamePrefix.Length);
                            }
                        }
                    }
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Delete a table.
    /// </summary>
    /// <param name="tableName">Name of the table to delete</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    public async Task DeleteTableAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        tableName = WithSchemaAndTableNamePrefix(tableName);

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // SQL reviewed
                    cmd.CommandText = $"DROP TABLE IF EXISTS {tableName}";
#pragma warning restore CA2100

                    _log.LogTrace("Deleting table. SQL: {0}", cmd.CommandText);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Npgsql.PostgresException e) when (IsTableNotFoundException(e))
            {
                _log.LogTrace("Table not found: {0}", tableName);
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Upsert entry into a table.
    /// </summary>
    /// <param name="tableName">The name assigned to a table of entries</param>
    /// <param name="record">Record to create/update</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    public async Task UpsertAsync(
        string tableName,
        PostgresMemoryRecord record,
        CancellationToken cancellationToken = default)
    {
        tableName = WithSchemaAndTableNamePrefix(tableName);

        const string EmptyPayload = "{}";
        const string EmptyContent = "";
        string[] emptyTags = [];

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // SQL reviewed
                    cmd.CommandText = $@"
                        INSERT INTO {tableName}
                            ({_colId}, {_colEmbedding}, {_colTags}, {_colContent}, {_colPayload})
                            VALUES
                            (@id, @embedding, @tags, @content, @payload)
                        ON CONFLICT ({_colId})
                        DO UPDATE SET
                            {_colEmbedding} = @embedding,
                            {_colTags}      = @tags,
                            {_colContent}   = @content,
                            {_colPayload}   = @payload
                    ";

                    cmd.Parameters.AddWithValue("@id", record.Id);
                    cmd.Parameters.AddWithValue("@embedding", record.Embedding);
                    cmd.Parameters.AddWithValue("@tags", NpgsqlDbType.Array | NpgsqlDbType.Text, record.Tags.ToArray() ?? emptyTags);
                    cmd.Parameters.AddWithValue("@content", NpgsqlDbType.Text, CleanContent(record.Content) ?? EmptyContent);
                    cmd.Parameters.AddWithValue("@payload", NpgsqlDbType.Jsonb, record.Payload ?? EmptyPayload);
#pragma warning restore CA2100

                    _log.LogTrace("Upserting record '{0}' in table '{1}'", record.Id, tableName);

                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Npgsql.PostgresException e) when (IsTableNotFoundException(e))
            {
                throw new IndexNotFoundException(e.Message, e);
            }
            catch (Exception e)
            {
                throw new PostgresException(e.Message, e);
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Get a list of records
    /// </summary>
    /// <param name="tableName">Table containing the records to fetch</param>
    /// <param name="target">Source vector to compare for similarity</param>
    /// <param name="minSimilarity">Minimum similarity threshold</param>
    /// <param name="filterSql">SQL filter to apply</param>
    /// <param name="sqlUserValues">List of user values passed with placeholders to avoid SQL injection</param>
    /// <param name="limit">Max number of records to retrieve</param>
    /// <param name="offset">Records to skip from the top</param>
    /// <param name="withEmbeddings">Whether to include embedding vectors</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    public async IAsyncEnumerable<(PostgresMemoryRecord record, double similarity)> GetSimilarAsync(
        string tableName,
        Vector target,
        double minSimilarity,
        string? filterSql = null,
        Dictionary<string, object>? sqlUserValues = null,
        int limit = 1,
        int offset = 0,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        tableName = WithSchemaAndTableNamePrefix(tableName);

        if (limit <= 0) { limit = int.MaxValue; }

        // Column names
        string columns = withEmbeddings
            ? _columnsListWithEmbeddings
            : _columnsListNoEmbeddings;

        // Filtering logic, including filter by similarity
        filterSql = filterSql?.Trim().Replace(PostgresSchema.PlaceholdersTags, _colTags, StringComparison.Ordinal);

        var maxDistance = 1 - minSimilarity;

        var distanceFilter = $"{_colEmbedding} <=> @embedding < @maxDistance";
        filterSql = string.IsNullOrWhiteSpace(filterSql)
            ? distanceFilter
            : $"({filterSql}) AND {distanceFilter}";

        if (sqlUserValues == null) { sqlUserValues = []; }

        _log.LogTrace("Searching by similarity. Table: {0}. Threshold: {1}. Limit: {2}. Offset: {3}. Using SQL filter: {4}",
            tableName,
            minSimilarity,
            limit,
            offset,
            string.IsNullOrWhiteSpace(filterSql)
                ? "false"
                : "true");

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // SQL reviewed
                    string colDistance = "__distance";

                    // When using 1 - (embedding <=> target) the index is not being used, therefore we calculate
                    // the similarity (1 - distance) later. Furthermore, colDistance can't be used in the WHERE clause.
                    cmd.CommandText = @$"
                        SELECT {columns}, {_colEmbedding} <=> @embedding AS {colDistance}
                        FROM {tableName}
                        WHERE {filterSql}
                        ORDER BY {colDistance} ASC
                        LIMIT @limit
                        OFFSET @offset
                    ";

                    cmd.Parameters.AddWithValue("@embedding", target);
                    cmd.Parameters.AddWithValue("@maxDistance", maxDistance);
                    cmd.Parameters.AddWithValue("@limit", limit);
                    cmd.Parameters.AddWithValue("@offset", offset);

                    foreach (KeyValuePair<string, object> kv in sqlUserValues)
                    {
                        cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                    }
#pragma warning restore CA2100
                    // TODO: rewrite code to stream results (need to combine yield and try-catch)
                    var result = new List<(PostgresMemoryRecord record, double similarity)>();

                    try
                    {
                        NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                        await using (dataReader.ConfigureAwait(false))
                        {
                            while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                double distance = dataReader.GetDouble(dataReader.GetOrdinal(colDistance));
                                double similarity = 1 - distance;
                                result.Add((ReadEntry(dataReader, withEmbeddings), similarity));
                            }
                        }
                    }
                    catch (Npgsql.PostgresException e) when (IsTableNotFoundException(e))
                    {
                        _log.LogTrace("Table not found: {0}", tableName);
                    }

                    // TODO: rewrite code to stream results (need to combine yield and try-catch)
                    foreach (var x in result)
                    {
                        yield return x;

                        // If requested cancel potentially long-running loop
                        if (cancellationToken is { IsCancellationRequested: true })
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Get a list of records
    /// </summary>
    /// <param name="tableName">Table containing the records to fetch</param>
    /// <param name="filterSql">SQL filter to apply</param>
    /// <param name="sqlUserValues">List of user values passed with placeholders to avoid SQL injection</param>
    /// <param name="orderBySql">SQL to order the records</param>
    /// <param name="limit">Max number of records to retrieve</param>
    /// <param name="offset">Records to skip from the top</param>
    /// <param name="withEmbeddings">Whether to include embedding vectors</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    public async IAsyncEnumerable<PostgresMemoryRecord> GetListAsync(
        string tableName,
        string? filterSql = null,
        Dictionary<string, object>? sqlUserValues = null,
        string? orderBySql = null,
        int limit = 1,
        int offset = 0,
        bool withEmbeddings = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        tableName = WithSchemaAndTableNamePrefix(tableName);

        if (limit <= 0) { limit = int.MaxValue; }

        string columns = withEmbeddings
            ? _columnsListWithEmbeddings
            : _columnsListNoEmbeddings;

        // Filtering logic
        filterSql = filterSql?.Trim().Replace(PostgresSchema.PlaceholdersTags, _colTags, StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(filterSql))
        {
            filterSql = "TRUE";
        }

        // Custom ordering
        if (string.IsNullOrWhiteSpace(orderBySql))
        {
            orderBySql = _colId;
        }

        _log.LogTrace("Fetching list of records. Table: {0}. Order by: {1}. Limit: {2}. Offset: {3}. Using SQL filter: {4}",
            tableName,
            orderBySql,
            limit,
            offset,
            string.IsNullOrWhiteSpace(filterSql)
                ? "false"
                : "true");

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // SQL reviewed
                    cmd.CommandText = @$"
                        SELECT {columns} FROM {tableName}
                        WHERE {filterSql}
                        ORDER BY {orderBySql}
                        LIMIT @limit
                        OFFSET @offset
                    ";

                    cmd.Parameters.AddWithValue("@limit", limit);
                    cmd.Parameters.AddWithValue("@offset", offset);

                    if (sqlUserValues != null)
                    {
                        foreach (KeyValuePair<string, object> kv in sqlUserValues)
                        {
                            cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                        }
                    }
#pragma warning restore CA2100

                    // TODO: rewrite code to stream results (need to combine yield and try-catch)
                    var result = new List<PostgresMemoryRecord>();

                    try
                    {
                        NpgsqlDataReader dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                        await using (dataReader.ConfigureAwait(false))
                        {
                            while (await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                result.Add(ReadEntry(dataReader, withEmbeddings));
                            }
                        }
                    }
                    catch (Npgsql.PostgresException e) when (IsTableNotFoundException(e))
                    {
                        _log.LogTrace("Table not found: {0}", tableName);
                    }

                    // TODO: rewrite code to stream results (need to combine yield and try-catch)
                    foreach (var x in result)
                    {
                        yield return x;

                        // If requested cancel potentially long-running loop
                        if (cancellationToken is { IsCancellationRequested: true })
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Delete an entry
    /// </summary>
    /// <param name="tableName">The name assigned to a table of entries</param>
    /// <param name="id">The key of the entry to delete</param>
    /// <param name="cancellationToken">Async task cancellation token</param>
    public async Task DeleteAsync(
        string tableName,
        string id,
        CancellationToken cancellationToken = default)
    {
        tableName = WithSchemaAndTableNamePrefix(tableName);
        _log.LogTrace("Deleting record '{0}' from table '{1}'", id, tableName);

        NpgsqlConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using (connection)
        {
            try
            {
                NpgsqlCommand cmd = connection.CreateCommand();

                await using (cmd.ConfigureAwait(false))
                {
#pragma warning disable CA2100 // SQL reviewed
                    cmd.CommandText = $"DELETE FROM {tableName} WHERE {_colId}=@id";
                    cmd.Parameters.AddWithValue("@id", id);
#pragma warning restore CA2100

                    try
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Npgsql.PostgresException e) when (IsTableNotFoundException(e))
                    {
                        _log.LogTrace("Table not found: {0}", tableName);
                    }
                }
            }
            finally
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }


    /// <inheritdoc/>
    public void Dispose()
    {
        _dataSource?.Dispose();
    }


    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
        catch (NullReferenceException)
        {
            // ignore
        }
    }


    #region private ================================================================================

    // See: https://www.postgresql.org/docs/current/errcodes-appendix.html
    private const string PgErrUndefinedTable = "42P01"; // undefined_table
    private const string PgErrUniqueViolation = "23505"; // unique_violation
    private const string PgErrTypeDoesNotExist = "42704"; // undefined_object
    private const string PgErrDatabaseDoesNotExist = "3D000"; // invalid_catalog_name

    private readonly string _schema;
    private readonly string _tableNamePrefix;
    private readonly string _createTableSql;
    private readonly string _colId;
    private readonly string _colEmbedding;
    private readonly string _colTags;
    private readonly string _colContent;
    private readonly string _colPayload;
    private readonly string _columnsListNoEmbeddings;
    private readonly string _columnsListWithEmbeddings;
    private readonly bool _dbNamePresent;


    /// <summary>
    /// Try to connect to PG, handling exceptions in case the DB doesn't exist
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task<NpgsqlConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Npgsql.PostgresException e) when (IsDbNotFoundException(e))
        {
            if (_dbNamePresent)
            {
                _log.LogCritical("DB not found. Try checking the connection string, e.g. whether the `Database` parameter is empty or incorrect: {0}", e.Message);
            }
            else
            {
                _log.LogCritical("DB not found. Try checking the connection string, e.g. specifying the `Database` parameter: {0}", e.Message);
            }

            throw;
        }
    }


    private static string CleanContent(string input)
    {
        // Remove 0x00 null, not supported by Postgres text fields, to avoid
        // exception: 22021: invalid byte sequence for encoding "UTF8": 0x00
        return input.Replace("\0", "", StringComparison.Ordinal);
    }


    private PostgresMemoryRecord ReadEntry(NpgsqlDataReader dataReader, bool withEmbeddings)
    {
        string id = dataReader.GetString(dataReader.GetOrdinal(_colId));
        string content = dataReader.GetString(dataReader.GetOrdinal(_colContent));
        string payload = dataReader.GetString(dataReader.GetOrdinal(_colPayload));
        List<string> tags = dataReader.GetFieldValue<List<string>>(dataReader.GetOrdinal(_colTags));

        Vector embedding = withEmbeddings
            ? dataReader.GetFieldValue<Vector>(dataReader.GetOrdinal(_colEmbedding))
            : new Vector(new ReadOnlyMemory<float>());

        return new PostgresMemoryRecord
        {
            Id = id,
            Embedding = embedding,
            Tags = tags,
            Content = content,
            Payload = payload
        };
    }


    /// <summary>
    /// Get full table name with schema from table name
    /// </summary>
    /// <param name="tableName"></param>
    /// <returns>Valid table name including schema</returns>
    private string WithSchemaAndTableNamePrefix(string tableName)
    {
        tableName = WithTableNamePrefix(tableName);
        PostgresSchema.ValidateTableName(tableName);

        return $"{_schema}.\"{tableName}\"";
    }


    private string WithTableNamePrefix(string tableName)
    {
        return $"{_tableNamePrefix}{tableName}";
    }


    private static bool IsDbNotFoundException(Npgsql.PostgresException e)
    {
        return e.SqlState == PgErrDatabaseDoesNotExist;
    }


    private static bool IsTableNotFoundException(Npgsql.PostgresException e)
    {
        return e.SqlState == PgErrUndefinedTable || e.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }


    private static bool IsVectorTypeDoesNotExistException(Npgsql.PostgresException e)
    {
        return e.SqlState == PgErrTypeDoesNotExist
            && e.Message.Contains("type", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("vector", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Generate a consistent lock id for a given resource, reducing the chance of collisions.
    /// If a collision happens because two resources have the same lock id, when locks are used
    /// these resources will be accessible one at a time, and not concurrently.
    /// </summary>
    /// <param name="resourceId">Resource Id</param>
    /// <returns>A number assigned to the resource</returns>
    private static long GenLockId(string resourceId)
    {
        return BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId)), 0)
            % short.MaxValue;
    }

    #endregion


}
