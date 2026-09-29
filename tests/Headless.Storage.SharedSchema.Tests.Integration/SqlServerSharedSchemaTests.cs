// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Features;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Permissions;
using Headless.Sequences;
using Headless.Settings;
using Headless.Sql;
using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>One SQL Server container; each test creates and drops its own database inside it.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerSharedSchemaFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerSharedSchemaFixture>;

[Collection<SqlServerSharedSchemaFixture>]
public sealed class SqlServerSharedSchemaTests(SqlServerSharedSchemaFixture fixture) : SharedSchemaTestsBase
{
    protected override string TableType => "U";

    protected override string SequenceType => "SO";

    protected override IReadOnlyDictionary<string, string[]> ExpectedRawTables { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["AuditLog"] = ["AuditLogEntries"],
            ["Coordination"] = ["CoordinationDescriptor", "CoordinationLiveness", "CoordinationNodeGeneration"],
            ["DistributedLocks"] = [],
            ["Features"] = ["FeatureDefinitions", "FeatureGroupDefinitions", "FeatureValues"],
            ["Fencing"] = ["FencingLeases"],
            ["Idempotency"] = ["IdempotencyRecords"],
            ["Messaging"] =
            [
                "MessagingInboxAudit",
                "MessagingInboxOperationReceipts",
                "MessagingPublished",
                "MessagingReceived",
                "MessagingSchemaState",
            ],
            ["Permissions"] = ["PermissionDefinitions", "PermissionGrants", "PermissionGroupDefinitions"],
            ["Sequences"] = ["Sequences"],
            ["Settings"] = ["SettingDefinitions", "SettingValues"],
        };

    // The lock fence sequence carries the default key prefix, so locks with different prefixes stay independent.
    protected override IReadOnlyDictionary<string, string[]> ExpectedRawSequences { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DistributedLocks"] = ["headless_distlocks_fence_distributed_lock"],
            ["Fencing"] = ["FencingLeaseGenerations"],
            ["Idempotency"] = ["IdempotencyRecordGenerations"],
        };

    protected override async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = $"shared_schema_{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand($"CREATE DATABASE [{database}];", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = database }.ToString();
    }

    protected override async Task DropDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            $"""
            IF DB_ID(N'{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override void AddSharedConnection(IServiceCollection services, string connectionString)
    {
        services.AddSqlServerSql(connectionString);
    }

    protected override void AddRawFamilies(IServiceCollection services)
    {
        services.AddSqlServerUnitOfWork();
        services.AddHeadlessCoordination(setup => setup.UseSqlServer());
        services.AddHeadlessDistributedLocks(setup => setup.UseSqlServer());
        services.AddHeadlessAuditLog(setup => setup.UseSqlServer());
        services.AddHeadlessSequences(setup => setup.UseSqlServer());
        services.AddHeadlessFeatures(setup => setup.UseSqlServer());
        services.AddHeadlessPermissions(setup => setup.UseSqlServer());
        services.AddHeadlessSettings(setup => setup.UseSqlServer());
        services.AddHeadlessFencing(setup => setup.UseSqlServer());
        services.AddHeadlessIdempotency(setup => setup.UseSqlServer());
    }

    protected override void UseMessagingStorage(MessagingSetupBuilder setup)
    {
        setup.UseSqlServer();
    }

    protected override void UseJobsStore(DbContextOptionsBuilder db, string connectionString)
    {
        db.UseSqlServer(connectionString);
    }

    protected override async Task<IReadOnlyList<CatalogObject>> ReadCatalogAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        // sys.objects covers tables, sequences, constraints, procedures, and functions; table types and indexes are
        // not schema-scoped objects there, so they are read separately.
        const string sql = """
            SELECT s.name, o.name, RTRIM(o.type)
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.is_ms_shipped = 0
            UNION ALL
            SELECT s.name, t.name, 'TT'
            FROM sys.table_types t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_user_defined = 1
            UNION ALL
            SELECT s.name, i.name, 'IX'
            FROM sys.indexes i
                JOIN sys.objects o ON o.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.is_ms_shipped = 0 AND o.type = 'U' AND i.name IS NOT NULL AND i.is_primary_key = 0
                AND i.is_unique_constraint = 0
            ORDER BY 1, 2;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var objects = new List<CatalogObject>();

        while (await reader.ReadAsync(cancellationToken))
        {
            objects.Add(new CatalogObject(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return objects;
    }

    protected override async Task<IReadOnlyList<string>> ReadUserSchemasAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        // Built-in schemas are dbo, guest, INFORMATION_SCHEMA, sys (ids 1-4) and the fixed database roles
        // (ids 16384 and above); anything in between was created by a user.
        const string sql = "SELECT name FROM sys.schemas WHERE schema_id BETWEEN 5 AND 16383 ORDER BY name;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var schemas = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }
}
