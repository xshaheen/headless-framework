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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>One PostgreSQL container; each test creates and drops its own database inside it.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlSharedSchemaFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlSharedSchemaFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        // Three hosts run every family at once, and several families keep their own pool, so the default
        // max_connections of 100 would fail startup with 53300 before any initializer race is observed.
        return base.Configure()
            .WithDatabase("shared_schema_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "max_connections=500");
    }
}

[Collection<PostgreSqlSharedSchemaFixture>]
public sealed class PostgreSqlSharedSchemaTests(PostgreSqlSharedSchemaFixture fixture) : SharedSchemaTestsBase
{
    // PostgreSQL's NAMEDATALEN is 64, so an identifier of exactly 63 bytes is the sign of silent truncation.
    private const int _TruncatedIdentifierLength = 63;

    protected override string TableType => "r";

    protected override string SequenceType => "S";

    protected override IReadOnlyDictionary<string, string[]> ExpectedRawTables { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["AuditLog"] = ["audit_log_entries"],
            ["Coordination"] = ["coordination_descriptor", "coordination_liveness", "coordination_node_generation"],
            ["DistributedLocks"] = [],
            ["Features"] = ["feature_definitions", "feature_group_definitions", "feature_values"],
            ["Fencing"] = ["fencing_leases"],
            ["Idempotency"] = ["idempotency_records"],
            ["Messaging"] =
            [
                "messaging_inbox_audit",
                "messaging_inbox_operation_receipts",
                "messaging_published",
                "messaging_received",
                "messaging_schema_state",
            ],
            ["Permissions"] = ["permission_definitions", "permission_grants", "permission_group_definitions"],
            ["Sequences"] = ["sequences"],
            ["Settings"] = ["setting_definitions", "setting_values"],
        };

    protected override string[] ExpectedJobsTables { get; } =
    ["cron_job_occurrences", "cron_jobs", "time_job_idempotency_reservations", "time_jobs"];

    // The audit log's identity column owns an implicit sequence named after the table and column.
    protected override IReadOnlyDictionary<string, string[]> ExpectedRawSequences { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["AuditLog"] = ["audit_log_entries_id_seq"],
            ["DistributedLocks"] = ["headless_distributed_locks_fence"],
            ["Fencing"] = ["fencing_lease_generations"],
            ["Idempotency"] = ["idempotency_record_generations"],
        };

    protected override async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = $"shared_schema_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{database}";""", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ToString();
    }

    protected override async Task DropDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE);""",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override void AddSharedConnection(IServiceCollection services, string connectionString)
    {
        services.AddPostgreSqlSql(connectionString);
    }

    protected override void AddRawFamilies(IServiceCollection services)
    {
        services.AddPostgreSqlUnitOfWork();
        services.AddHeadlessCoordination(setup => setup.UsePostgreSql());
        services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());
        services.AddHeadlessAuditLog(setup => setup.UsePostgreSql());
        services.AddHeadlessSequences(setup => setup.UsePostgreSql());
        services.AddHeadlessFeatures(setup => setup.UsePostgreSql());
        services.AddHeadlessPermissions(setup => setup.UsePostgreSql());
        services.AddHeadlessSettings(setup => setup.UsePostgreSql());
        services.AddHeadlessFencing(setup => setup.UsePostgreSql());
        services.AddHeadlessIdempotency(setup => setup.UsePostgreSql());
    }

    protected override void UseMessagingStorage(MessagingSetupBuilder setup)
    {
        setup.UsePostgreSql();
    }

    protected override void UseJobsStore(DbContextOptionsBuilder db, string connectionString)
    {
        db.UseNpgsql(connectionString);
    }

    protected override async Task<IReadOnlyList<CatalogObject>> ReadCatalogAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        // Relations (tables, sequences, indexes), functions, and constraints: every object kind a family's DDL can
        // create. Objects that belong to an extension are left out: Messaging installs pg_trgm when it can, and an
        // extension is database-wide, created in the first schema on the search path rather than owned by a feature.
        const string sql = """
            SELECT n.nspname, c.relname, c.relkind::text
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
                AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'e')
            UNION ALL
            SELECT n.nspname, p.proname, 'f'
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
                AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
            UNION ALL
            SELECT n.nspname, t.conname, 'c:' || t.contype::text
            FROM pg_constraint t JOIN pg_namespace n ON n.oid = t.connamespace
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1, 2;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
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
        // "public" ships with every database; it must stay empty, which ReadCatalogAsync already asserts.
        const string sql = """
            SELECT nspname FROM pg_namespace
            WHERE nspname NOT LIKE 'pg\_%' AND nspname NOT IN ('information_schema', 'public')
            ORDER BY 1;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var schemas = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }

    protected override void AssertIdentifierLimits(IReadOnlyList<CatalogObject> objects)
    {
        objects
            .Should()
            .NotContain(
                o => System.Text.Encoding.UTF8.GetByteCount(o.Name) >= _TruncatedIdentifierLength,
                "PostgreSQL silently truncates identifiers to 63 bytes"
            );
    }
}
