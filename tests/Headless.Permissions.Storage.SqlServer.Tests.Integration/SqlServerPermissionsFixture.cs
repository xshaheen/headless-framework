// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting;
using Headless.Permissions;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerPermissionsFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerPermissionsFixture>,
        IPermissionsStorageFixture
{
    public Type ProviderExceptionType => typeof(SqlException);

    public StorageNamingStyle NamingStyle => StorageNamingStyle.PascalCase;

    public string OtherDatabaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "tempdb" }.ConnectionString;

    public void UseStorage(HeadlessPermissionsSetupBuilder setup, string connectionString)
    {
        setup.UseSqlServer(connectionString);
    }

    public void UseEntityFrameworkProvider(DbContextOptionsBuilder builder, string connectionString)
    {
        builder.UseSqlServer(connectionString);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        // 2601: duplicate key in a unique index; 2627: violation of a primary key or unique constraint.
        return exception is SqlException { Number: 2601 or 2627 };
    }

    public async Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // Every table goes (the schema runner's history and a test's counter table included), then the table types:
        // SQL Server refuses to drop a schema that still owns objects.
        await using var command = new SqlCommand(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'DROP TABLE [{schema}].[' + t.name + N'];'
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TYPE [{schema}].[' + t.name + N'];'
            FROM sys.types t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'{schema}' AND t.is_user_defined = 1;
            IF SCHEMA_ID(N'{schema}') IS NOT NULL SET @sql += N'DROP SCHEMA [{schema}];';
            EXEC sp_executesql @sql;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TableExistsAsync(string schema, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", tableName);

        return (bool)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<long> CountRowsAsync(string schema, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM [{schema}].[{tableName}];", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<PermissionsStoreObjects> ReadStoreObjectsAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await _ReadPairsAsync(
            connection,
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema",
            schema,
            cancellationToken
        );
        var indexes = await _ReadPairsAsync(
            connection,
            """
            SELECT t.name, i.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND i.name IS NOT NULL
            """,
            schema,
            cancellationToken
        );

        return new PermissionsStoreObjects(columns, indexes);
    }

    public async Task CreateInsertCommandCounterAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            CREATE TABLE [{schema}].[PermissionGrantInsertCommandCounter] ([Count] int NOT NULL);
            INSERT INTO [{schema}].[PermissionGrantInsertCommandCounter] ([Count]) VALUES (0);
            EXEC(N'
                CREATE TRIGGER [{schema}].[TR_PermissionGrant_InsertCommandCounter]
                ON [{schema}].[PermissionGrants]
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    UPDATE [{schema}].[PermissionGrantInsertCommandCounter] SET [Count] = [Count] + 1;
                END
            ');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ReadInsertCommandCountAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"SELECT [Count] FROM [{schema}].[PermissionGrantInsertCommandCounter];",
            connection
        );

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public DbConnection CreateConnection(string connectionString)
    {
        return new SqlConnection(connectionString);
    }

    public ValueTask<IUnitOfWork> BeginUnitOfWorkAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((SqlConnection)connection, cancellationToken);
    }

    public IUnitOfWork EnlistUnitOfWork(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((SqlConnection)connection, (SqlTransaction)transaction);
    }

    private static async Task<HashSet<string>> _ReadPairsAsync(
        SqlConnection connection,
        string sql,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        return pairs;
    }
}
