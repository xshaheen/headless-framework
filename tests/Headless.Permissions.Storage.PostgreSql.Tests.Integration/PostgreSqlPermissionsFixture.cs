// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Hosting;
using Headless.Permissions;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlPermissionsFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlPermissionsFixture>,
        IPermissionsStorageFixture
{
    public Type ProviderExceptionType => typeof(PostgresException);

    public StorageNamingStyle NamingStyle => StorageNamingStyle.SnakeCase;

    // The refusal tests only need a second database on the same server; a derived one keeps them off the shared
    // server's admin database.
    public string OtherDatabaseConnectionString => ConnectionStringFor(DerivedDatabaseName("other"));

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateDatabaseAsync(DerivedDatabaseName("other"), CancellationToken.None);
    }

    public void UseStorage(HeadlessPermissionsSetupBuilder setup, string connectionString)
    {
        setup.UsePostgreSql(connectionString);
    }

    public void UseEntityFrameworkProvider(DbContextOptionsBuilder builder, string connectionString)
    {
        builder.UseNpgsql(connectionString);
    }

    public async Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TableExistsAsync(string schema, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            )
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue("table", tableName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<long> CountRowsAsync(string schema, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""SELECT COUNT(*) FROM "{schema}"."{tableName}";""", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<PermissionsStoreObjects> ReadStoreObjectsAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await _ReadPairsAsync(
            connection,
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = @schema",
            schema,
            cancellationToken
        );
        var indexes = await _ReadPairsAsync(
            connection,
            "SELECT tablename, indexname FROM pg_indexes WHERE schemaname = @schema",
            schema,
            cancellationToken
        );

        return new PermissionsStoreObjects(columns, indexes);
    }

    public async Task CreateInsertCommandCounterAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            CREATE TABLE "{schema}"."PermissionGrantInsertCommandCounter" ("Count" integer NOT NULL);
            INSERT INTO "{schema}"."PermissionGrantInsertCommandCounter" ("Count") VALUES (0);

            CREATE FUNCTION "{schema}"."CountPermissionGrantInsertCommand"() RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                UPDATE "{schema}"."PermissionGrantInsertCommandCounter" SET "Count" = "Count" + 1;
                RETURN NULL;
            END;
            $function$;

            CREATE TRIGGER "TR_PermissionGrant_InsertCommandCounter"
            AFTER INSERT ON "{schema}".permission_grants
            FOR EACH STATEMENT
            EXECUTE FUNCTION "{schema}"."CountPermissionGrantInsertCommand"();
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ReadInsertCommandCountAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""SELECT "Count" FROM "{schema}"."PermissionGrantInsertCommandCounter";""",
            connection
        );

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public DbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public ValueTask<IUnitOfWork> BeginUnitOfWorkAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        return factory.BeginAsync((NpgsqlConnection)connection, cancellationToken);
    }

    public IUnitOfWork EnlistUnitOfWork(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction)
    {
        return factory.Enlist((NpgsqlConnection)connection, (NpgsqlTransaction)transaction);
    }

    private static async Task<HashSet<string>> _ReadPairsAsync(
        NpgsqlConnection connection,
        string sql,
        string schema,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(nameof(schema), schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        return pairs;
    }
}
