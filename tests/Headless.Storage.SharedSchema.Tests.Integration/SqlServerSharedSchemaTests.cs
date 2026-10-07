// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.AuditLog;
using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Features;
using Headless.Fencing;
using Headless.Idempotency;
using Headless.Messaging;
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
public sealed partial class SqlServerSharedSchemaTests(SqlServerSharedSchemaFixture fixture) : SharedSchemaTestsBase
{
    protected override string TableType => "U";

    protected override string SequenceType => "SO";

    protected override IReadOnlyDictionary<string, string[]> ExpectedRawTables { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SchemaRunner"] = ["headless_schema_history"],
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
            ],
            ["Permissions"] = ["PermissionDefinitions", "PermissionGrants", "PermissionGroupDefinitions"],
            ["Sequences"] = ["Sequences"],
            ["Settings"] = ["SettingDefinitions", "SettingValues"],
        };

    protected override string[] ExpectedJobsTables { get; } =
    ["CronJobOccurrences", "CronJobs", "TimeJobIdempotencyReservations", "TimeJobs"];

    // The lock fence sequence carries the default key prefix, so locks with different prefixes stay independent.
    protected override IReadOnlyDictionary<string, string[]> ExpectedRawSequences { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DistributedLocks"] = ["DistributedLocksFence_distributed_lock"],
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
        services.AddUnitOfWork();
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

    protected override async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override async Task<IReadOnlyList<string>> QueryLinesAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var lines = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }

    // Constraint names the engine generates (an unnamed default or primary key) differ per database, so they are
    // masked; everything else is compared as the catalog reports it. Catalog columns carry different collations
    // (names follow the database, descriptions the server's metadata collation), so each is coerced before CONCAT.
    protected override string SchemaShapeSql =>
        """
            SELECT line FROM (
                SELECT CONCAT('table ', s.name COLLATE DATABASE_DEFAULT, '.', t.name COLLATE DATABASE_DEFAULT) AS line
                FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                UNION ALL
                SELECT CONCAT(
                    'column ', s.name COLLATE DATABASE_DEFAULT, '.', o.name COLLATE DATABASE_DEFAULT, '.', c.name COLLATE DATABASE_DEFAULT,
                    ' type=', ty.name COLLATE DATABASE_DEFAULT, '(', c.max_length, ',', c.precision, ',', c.scale, ')',
                    IIF(c.is_nullable = 1, ' null', ' not null'), IIF(c.is_identity = 1, ' identity', ''),
                    ' collation=', c.collation_name COLLATE DATABASE_DEFAULT,
                    ' default=', dc.definition COLLATE DATABASE_DEFAULT, IIF(dc.is_system_named = 0, ' default-name=' + dc.name COLLATE DATABASE_DEFAULT, ''),
                    ' computed=', cc.definition COLLATE DATABASE_DEFAULT, ' position=', c.column_id)
                FROM sys.columns c
                    JOIN sys.objects o ON o.object_id = c.object_id
                    JOIN sys.schemas s ON s.schema_id = o.schema_id
                    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                    LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
                    LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
                WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V')
                UNION ALL
                SELECT CONCAT(
                    'index ', s.name COLLATE DATABASE_DEFAULT, '.', t.name COLLATE DATABASE_DEFAULT, '.', IIF(kc.is_system_named = 1, '<system>', i.name COLLATE DATABASE_DEFAULT),
                    ' ', i.type_desc COLLATE DATABASE_DEFAULT, IIF(i.is_unique = 1, ' unique', ''), IIF(i.is_primary_key = 1, ' primary', ''),
                    IIF(i.is_unique_constraint = 1, ' unique-constraint', ''), ' ignore-dup=', i.ignore_dup_key,
                    ' keys=', (
                        SELECT STRING_AGG(CONCAT(col.name COLLATE DATABASE_DEFAULT, IIF(ic.is_descending_key = 1, ' desc', '')), ',')
                            WITHIN GROUP (ORDER BY ic.key_ordinal)
                        FROM sys.index_columns ic
                            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
                    ),
                    ' include=', (
                        SELECT STRING_AGG(col.name COLLATE DATABASE_DEFAULT, ',') WITHIN GROUP (ORDER BY col.name COLLATE DATABASE_DEFAULT)
                        FROM sys.index_columns ic
                            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
                    ),
                    ' filter=', i.filter_definition COLLATE DATABASE_DEFAULT)
                FROM sys.indexes i
                    JOIN sys.tables t ON t.object_id = i.object_id
                    JOIN sys.schemas s ON s.schema_id = t.schema_id
                    LEFT JOIN sys.key_constraints kc
                        ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
                WHERE t.is_ms_shipped = 0 AND i.type > 0
                UNION ALL
                SELECT CONCAT(
                    'check ', s.name COLLATE DATABASE_DEFAULT, '.', OBJECT_NAME(ck.parent_object_id) COLLATE DATABASE_DEFAULT, '.',
                    IIF(ck.is_system_named = 1, '<system>', ck.name COLLATE DATABASE_DEFAULT), ' ', ck.definition COLLATE DATABASE_DEFAULT)
                FROM sys.check_constraints ck JOIN sys.schemas s ON s.schema_id = ck.schema_id
                UNION ALL
                SELECT CONCAT(
                    'foreign-key ', s.name COLLATE DATABASE_DEFAULT, '.', OBJECT_NAME(fk.parent_object_id) COLLATE DATABASE_DEFAULT, '.',
                    IIF(fk.is_system_named = 1, '<system>', fk.name COLLATE DATABASE_DEFAULT), ' -> ',
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id) COLLATE DATABASE_DEFAULT, '.', OBJECT_NAME(fk.referenced_object_id) COLLATE DATABASE_DEFAULT,
                    ' delete=', fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT)
                FROM sys.foreign_keys fk JOIN sys.schemas s ON s.schema_id = fk.schema_id
                UNION ALL
                SELECT CONCAT(
                    'sequence ', s.name COLLATE DATABASE_DEFAULT, '.', sq.name COLLATE DATABASE_DEFAULT, ' type=', TYPE_NAME(sq.user_type_id) COLLATE DATABASE_DEFAULT,
                    ' start=', CONVERT(nvarchar(64), sq.start_value), ' increment=', CONVERT(nvarchar(64), sq.increment),
                    ' min=', CONVERT(nvarchar(64), sq.minimum_value), ' max=', CONVERT(nvarchar(64), sq.maximum_value),
                    ' cycle=', sq.is_cycling, ' cached=', sq.is_cached, ' cache=', sq.cache_size)
                FROM sys.sequences sq JOIN sys.schemas s ON s.schema_id = sq.schema_id
                UNION ALL
                SELECT CONCAT(
                    'module ', s.name COLLATE DATABASE_DEFAULT, '.', o.name COLLATE DATABASE_DEFAULT, ' ', RTRIM(o.type) COLLATE DATABASE_DEFAULT, ' ',
                    CONVERT(varchar(64), HASHBYTES('SHA2_256', m.definition), 2))
                FROM sys.sql_modules m
                    JOIN sys.objects o ON o.object_id = m.object_id
                    JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE o.is_ms_shipped = 0
                UNION ALL
                SELECT CONCAT(
                    'table-type ', s.name COLLATE DATABASE_DEFAULT, '.', tt.name COLLATE DATABASE_DEFAULT, '.', c.name COLLATE DATABASE_DEFAULT, ' type=', TYPE_NAME(c.user_type_id) COLLATE DATABASE_DEFAULT,
                    '(', c.max_length, ',', c.precision, ',', c.scale, ')', IIF(c.is_nullable = 1, ' null', ' not null'),
                    ' position=', c.column_id)
                FROM sys.table_types tt
                    JOIN sys.schemas s ON s.schema_id = tt.schema_id
                    JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                WHERE tt.is_user_defined = 1
            ) shape
            ORDER BY line COLLATE Latin1_General_100_BIN2;
            """;

    protected override string HistoryLinesSql(string schema)
    {
        return $"""
            SELECT CONCAT([Feature], '/', [StepVersion], ' ', [Checksum] COLLATE Latin1_General_100_BIN2, ' ', [Description] COLLATE Latin1_General_100_BIN2)
            FROM [{schema}].[headless_schema_history]
            ORDER BY [Feature], [StepVersion];
            """;
    }

    protected override string DeleteHistoryRowSql(string schema, string feature, string version)
    {
        return $"""
            DELETE FROM [{schema}].[headless_schema_history]
            WHERE [Feature] = N'{feature}' AND [StepVersion] = N'{version}';
            """;
    }

    protected override string SetChecksumSql(string schema, string feature, string version, string checksum)
    {
        return $"""
            UPDATE [{schema}].[headless_schema_history] SET [Checksum] = '{checksum}'
            WHERE [Feature] = N'{feature}' AND [StepVersion] = N'{version}';
            """;
    }

    protected override async Task ExecuteScriptWithPlainClientAsync(
        string connectionString,
        string script,
        CancellationToken cancellationToken
    )
    {
        // sqlcmd's client-side contract: split on lines that hold only GO and send each batch as-is. Azure SQL Edge
        // ships no sqlcmd, so this reproduces it without any Headless code on the path.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in GoSeparatorRegex.Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [GeneratedRegex(
        @"^[ \t]*GO[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex GoSeparatorRegex { get; }
}
