// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>SQL Server container shared by the DistributedLocks integration tests.</summary>
/// <remarks>
/// The container is reused across runs of this checkout, so it starts by dropping the default schema's fence sequences
/// and its schema history. The tests apply the schema without dropping it, and the runner trusts its history: a step
/// changed since an older run would otherwise fail its checksum.
/// </remarks>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerDistributedLockFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerDistributedLockFixture>,
        IAsyncLifetime
{
    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"""
            DECLARE @drop nvarchar(max) = N'';
            SELECT @drop += N'DROP SEQUENCE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';'
            FROM sys.sequences WHERE SCHEMA_NAME(schema_id) = N'{HeadlessStorageDefaults.Schema}';
            EXEC sp_executesql @drop;
            DROP TABLE IF EXISTS [{HeadlessStorageDefaults.Schema}].[{SchemaRunner.HistoryTableName}];
            """,
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
