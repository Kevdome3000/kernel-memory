// Copyright (c) Microsoft.All rights reserved.

using Microsoft.Data.SqlClient;
using Microsoft.KernelMemory.Models;

namespace Microsoft.KernelMemory.MemoryDb.SQLServer.QueryProviders;

public interface ISqlServerQueryProvider
{
    /// <summary>
    /// Return SQL used to create a new index
    /// </summary>
    string PrepareCreateIndexQuery(
        int sqlServerVersion,
        string index,
        int vectorSize);


    /// <summary>
    /// Return SQL used to get a list of indexes
    /// </summary>
    string PrepareGetIndexesQuery();


    /// <summary>
    /// Return SQL used to delete an index
    /// </summary>
    string PrepareDeleteIndexQuery(string index);


    /// <summary>
    /// Return SQL used to delete a memory record
    /// </summary>
    string PrepareDeleteRecordQuery(string index);


    /// <summary>
    /// Return SQL used to get a list of memory records
    /// </summary>
    string PrepareGetRecordsListQuery(
        string index,
        ICollection<MemoryFilter>? filters,
        bool withEmbedding,
        SqlParameterCollection parameters);


    /// <summary>
    /// Return SQL used to get a list of similar memory records
    /// </summary>
    string PrepareGetSimilarRecordsListQuery(
        string index,
        ICollection<MemoryFilter>? filters,
        bool withEmbedding,
        SqlParameterCollection parameters);


    /// <summary>
    /// Return SQL used to upsert a batch of memory records
    /// </summary>
    string PrepareUpsertRecordsBatchQuery(string index);


    /// <summary>
    /// Return SQL used to create all supporting tables
    /// </summary>
    string PrepareCreateAllSupportingTablesQuery();
}
