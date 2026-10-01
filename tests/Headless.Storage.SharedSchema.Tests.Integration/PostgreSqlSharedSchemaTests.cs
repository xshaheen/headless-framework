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
            ["SchemaRunner"] = ["headless_schema_history"],
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

    protected override async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override async Task<IReadOnlyList<string>> QueryLinesAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var lines = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }

    // Objects owned by an extension are described by the extension line alone: Messaging installs pg_trgm when it
    // can, and its functions are the extension's, not a feature's.
    protected override string SchemaShapeSql =>
        """
            WITH rels AS (
                SELECT c.oid, n.nspname, c.relname, c.relkind
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
                    AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype = 'e')
            )
            SELECT line FROM (
                SELECT 'relation ' || r.nspname || '.' || r.relname || ' kind=' || r.relkind::text AS line
                FROM rels r
                UNION ALL
                SELECT 'column ' || r.nspname || '.' || r.relname || '.' || a.attname
                    || ' type=' || format_type(a.atttypid, a.atttypmod)
                    || CASE WHEN a.attnotnull THEN ' not null' ELSE ' null' END
                    || coalesce(' default=' || pg_get_expr(ad.adbin, ad.adrelid), '')
                    || CASE WHEN a.attidentity <> '' THEN ' identity=' || a.attidentity::text ELSE '' END
                    || CASE WHEN a.attgenerated <> '' THEN ' generated=' || a.attgenerated::text ELSE '' END
                    || coalesce(' collation=' || co.collname, '')
                    || ' position=' || a.attnum
                FROM rels r
                    JOIN pg_attribute a ON a.attrelid = r.oid AND a.attnum > 0 AND NOT a.attisdropped
                    JOIN pg_type ty ON ty.oid = a.atttypid
                    LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                    LEFT JOIN pg_collation co ON co.oid = a.attcollation AND a.attcollation <> ty.typcollation
                WHERE r.relkind IN ('r', 'p', 'v', 'm')
                UNION ALL
                SELECT 'index ' || r.nspname || '.' || r.relname || ' ' || pg_get_indexdef(r.oid)
                FROM rels r
                WHERE r.relkind IN ('i', 'I')
                UNION ALL
                SELECT 'constraint ' || n.nspname || '.' || t.conname || ' on ' || coalesce(t.conrelid::regclass::text, '-')
                    || ' ' || pg_get_constraintdef(t.oid)
                FROM pg_constraint t JOIN pg_namespace n ON n.oid = t.connamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
                UNION ALL
                SELECT 'sequence ' || s.schemaname || '.' || s.sequencename || ' type=' || s.data_type::text
                    || ' start=' || s.start_value || ' min=' || s.min_value || ' max=' || s.max_value
                    || ' increment=' || s.increment_by || ' cycle=' || s.cycle || ' cache=' || s.cache_size
                FROM pg_sequences s
                WHERE s.schemaname NOT IN ('pg_catalog', 'information_schema')
                UNION ALL
                SELECT 'routine ' || n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid)
                    || ') ' || md5(pg_get_functiondef(p.oid))
                FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND p.prokind IN ('f', 'p')
                    AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
                UNION ALL
                SELECT 'extension ' || e.extname || ' in ' || n.nspname
                FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace
            ) shape
            ORDER BY line COLLATE "C";
            """;

    protected override string HistoryLinesSql(string schema)
    {
        return $"""
            SELECT feature || '/' || step_version || ' ' || checksum || ' ' || description
            FROM "{schema}".headless_schema_history
            ORDER BY feature, step_version;
            """;
    }

    protected override string DeleteHistoryRowSql(string schema, string feature, string version)
    {
        return $"""
            DELETE FROM "{schema}".headless_schema_history WHERE feature = '{feature}' AND step_version = '{version}';
            """;
    }

    protected override string SetChecksumSql(string schema, string feature, string version, string checksum)
    {
        return $"""
            UPDATE "{schema}".headless_schema_history SET checksum = '{checksum}'
            WHERE feature = '{feature}' AND step_version = '{version}';
            """;
    }

    protected override async Task ExecuteScriptWithPlainClientAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken
    )
    {
        // psql inside the container, the client a DBA would use. ON_ERROR_STOP turns a failed statement into a
        // non-zero exit code instead of an ERROR line on stderr that psql otherwise skips past.
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        var path = $"/tmp/{database}-{Guid.NewGuid():N}.sql";
        await fixture.Container.CopyAsync(System.Text.Encoding.UTF8.GetBytes(script), path, ct: cancellationToken);

        var result = await fixture.Container.ExecAsync(
            ["psql", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", database, "-f", path],
            cancellationToken
        );

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stderr.Should().NotContain("ERROR");
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
