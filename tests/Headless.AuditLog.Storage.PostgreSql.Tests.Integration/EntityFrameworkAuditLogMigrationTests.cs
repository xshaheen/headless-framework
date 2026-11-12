// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

/// <summary>Builds the services an application with Entity Framework audit storage on PostgreSQL registers.</summary>
internal static class EntityFrameworkAuditLogServices
{
    public static ServiceProvider Create<TContext>(
        string connectionString,
        string schema,
        bool retryOnFailure,
        Action<AuditLogStorageOptions>? configureStorage = null,
        TimeProvider? clock = null
    )
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddDbContextFactory<TContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    // Inside the audit schema, so dropping the schema resets the migrations too.
                    npgsql.MigrationsHistoryTable(AuditLogMigrationScripts.HistoryTable, schema);

                    if (retryOnFailure)
                    {
                        npgsql.EnableRetryOnFailure();
                    }
                }
            )
        );
        services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;
                configureStorage?.Invoke(options);
            });
            setup.UsePostgreSqlEntityFramework<TContext>();
        });

        return services.BuildServiceProvider();
    }

    public static async Task MigrateAsync<TContext>(
        IServiceProvider services,
        string? targetMigration,
        CancellationToken cancellationToken
    )
        where TContext : DbContext
    {
        var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);
    }

    public static async Task AddAsync<TContext>(
        IServiceProvider services,
        CancellationToken cancellationToken,
        params (string Action, DateTime CreatedAt)[] entries
    )
        where TContext : DbContext
    {
        var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        foreach (var (action, createdAt) in entries)
        {
            context.Set<AuditLogEntry>().Add(new AuditLogEntry { Action = action, CreatedAt = createdAt });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}

[Collection<PostgreSqlAuditLogFixture>]
public sealed class EntityFrameworkAuditLogAppendOnlyMigrationTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    private const string _Schema = AppendOnlyAuditDbContext.Schema;
    private static readonly DateTime _Expired = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData($"""UPDATE "{_Schema}"."audit_log_entries" SET "action" = 'tampered';""")]
    [InlineData($"""DELETE FROM "{_Schema}"."audit_log_entries";""")]
    [InlineData($"""TRUNCATE "{_Schema}"."audit_log_entries";""")]
    public async Task should_refuse_changes_to_written_rows_after_the_migration_added_the_trigger(string statement)
    {
        // given
        await using var services = await _MigrateAsync(retryOnFailure: false);
        await EntityFrameworkAuditLogServices.AddAsync<AppendOnlyAuditDbContext>(
            services,
            AbortToken,
            ("entity.created", _Expired)
        );

        // when
        var act = () => PostgreSqlTestDatabase.ExecuteAsync(fixture.ConnectionString, statement, AbortToken);

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _ActionsAsync()).Should().Equal("entity.created");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_purge_expired_rows_through_the_trigger_with_or_without_a_retrying_strategy(
        bool retryOnFailure
    )
    {
        // given - a retrying execution strategy refuses a transaction the caller opened itself
        await using var services = await _MigrateAsync(retryOnFailure);
        await EntityFrameworkAuditLogServices.AddAsync<AppendOnlyAuditDbContext>(
            services,
            AbortToken,
            ("expired.first", _Expired),
            ("expired.second", _Expired.AddMinutes(1)),
            ("recent", DateTime.UtcNow)
        );
        var retention = services.GetRequiredService<IAuditLogRetention<AppendOnlyAuditDbContext>>();

        // when
        var first = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 1, AbortToken);
        var second = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);
        var third = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);

        // then
        first.Should().Be(1);
        second.Should().Be(1);
        third.Should().Be(0);
        (await _ActionsAsync()).Should().Equal("recent");
    }

    [Fact]
    public async Task should_keep_one_guard_when_a_later_migration_runs_the_step_again()
    {
        // given - a later migration that calls the helper again, as one changing the purge authority would
        await using var services = await _MigrateAsync(retryOnFailure: false);
        var migrationBuilder = new MigrationBuilder(HeadlessStorageNaming.NpgsqlEfProviderName);
        migrationBuilder.AddHeadlessAuditLogAppendOnly(schema: _Schema);
        var sql = ((SqlOperation)migrationBuilder.Operations[0]).Sql;

        // when
        await PostgreSqlTestDatabase.ExecuteAsync(fixture.ConnectionString, sql, AbortToken);
        var delete = () =>
            PostgreSqlTestDatabase.ExecuteAsync(
                fixture.ConnectionString,
                $"""DELETE FROM "{_Schema}"."audit_log_entries";""",
                AbortToken
            );

        // then
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"""
                SELECT tgname::text FROM pg_trigger
                WHERE tgrelid = '"{_Schema}"."audit_log_entries"'::regclass AND NOT tgisinternal
                ORDER BY tgname;
                """,
                AbortToken
            )
        )
            .Should()
            .Equal("audit_log_entries_append_only", "audit_log_entries_append_only_truncate");
        await EntityFrameworkAuditLogServices.AddAsync<AppendOnlyAuditDbContext>(
            services,
            AbortToken,
            ("entity.created", _Expired)
        );
        await delete.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task should_remove_the_trigger_and_its_function_when_the_migration_is_reverted()
    {
        // given
        await using var services = await _MigrateAsync(retryOnFailure: false);

        // when
        await EntityFrameworkAuditLogServices.MigrateAsync<AppendOnlyAuditDbContext>(
            services,
            Migration.InitialDatabase,
            AbortToken
        );

        // then
        (
            await PostgreSqlTestDatabase.ScalarAsync<long>(
                fixture.ConnectionString,
                $"""
                SELECT count(*) FROM pg_proc AS p JOIN pg_namespace AS n ON n.oid = p.pronamespace
                WHERE n.nspname = '{_Schema}';
                """,
                AbortToken
            )
        )
            .Should()
            .Be(0);
        (
            await PostgreSqlTestDatabase.ScalarAsync<bool>(
                fixture.ConnectionString,
                $"""SELECT to_regclass('"{_Schema}"."audit_log_entries"') IS NULL;""",
                AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    private async Task<ServiceProvider> _MigrateAsync(bool retryOnFailure)
    {
        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.ConnectionString,
            $"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""",
            AbortToken
        );

        var services = EntityFrameworkAuditLogServices.Create<AppendOnlyAuditDbContext>(
            fixture.ConnectionString,
            _Schema,
            retryOnFailure
        );
        await EntityFrameworkAuditLogServices.MigrateAsync<AppendOnlyAuditDbContext>(
            services,
            targetMigration: null,
            AbortToken
        );

        return services;
    }

    private Task<List<string>> _ActionsAsync()
    {
        return PostgreSqlTestDatabase.ListAsync(
            fixture.ConnectionString,
            $"""SELECT "action" FROM "{_Schema}"."audit_log_entries" ORDER BY "created_at", "id";""",
            AbortToken
        );
    }
}

/// <summary>
/// Entity Framework storage in the least-privilege deployment: the owner login runs the migrations, and the
/// application login, which owns nothing and has no DDL rights, runs the retention and keeps the partitions.
/// </summary>
[Collection<PostgreSqlAuditLogRolesFixture>]
public sealed class EntityFrameworkAuditLogLeastPrivilegeTests(PostgreSqlAuditLogRolesFixture fixture) : TestBase
{
    private const string _Table = "audit_log_entries";
    private static readonly DateTime _Expired = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task should_purge_only_through_the_owners_function_when_the_migration_gave_the_owner_the_purge()
    {
        // given
        const string schema = OwnerPurgeAuditDbContext.Schema;
        await using var owner = await _MigrateAsOwnerAsync<OwnerPurgeAuditDbContext>(schema);
        await using var app = EntityFrameworkAuditLogServices.Create<OwnerPurgeAuditDbContext>(
            fixture.AppConnectionString,
            schema,
            retryOnFailure: true,
            options => options.PurgeAuthority = AuditLogPurgeAuthority.Owner
        );
        await EntityFrameworkAuditLogServices.AddAsync<OwnerPurgeAuditDbContext>(
            app,
            AbortToken,
            ("expired", _Expired),
            ("ten.days.old", DateTime.UtcNow.AddDays(-10))
        );
        var retention = app.GetRequiredService<IAuditLogRetention<OwnerPurgeAuditDbContext>>();

        // when
        var settingAlone = () =>
            PostgreSqlTestDatabase.ExecuteAsync(
                fixture.AppConnectionString,
                $"""
                BEGIN;
                SELECT pg_catalog.set_config('headless.audit_log_purge', 'on', true);
                DELETE FROM "{schema}"."{_Table}";
                COMMIT;
                """,
                AbortToken
            );
        var insideMinimumRetention = () => retention.PurgeAsync(TimeSpan.FromDays(1), limit: 10, AbortToken);
        await settingAlone.Should().ThrowAsync<PostgresException>();
        await insideMinimumRetention.Should().ThrowAsync<PostgresException>();
        var deleted = await retention.PurgeAsync(TimeSpan.FromDays(31), limit: 10, AbortToken);

        // then
        deleted.Should().Be(1);
        (await _ActionsAsync(schema)).Should().Equal("ten.days.old");
    }

    [Fact]
    public async Task should_add_partitions_to_the_migrated_table_and_keep_the_keys_and_indexes_the_model_maps()
    {
        // given, when
        const string schema = PartitionedAuditDbContext.Schema;
        await using var owner = await _MigrateAsOwnerAsync<PartitionedAuditDbContext>(schema);
        var factory = owner.GetRequiredService<IDbContextFactory<PartitionedAuditDbContext>>();
        await using var context = await factory.CreateDbContextAsync(AbortToken);
        var entity = context.Model.FindEntityType(typeof(AuditLogEntry))!;
        var mapped = entity
            .GetIndexes()
            .Select(index => index.GetDatabaseName()!)
            .Append(entity.FindPrimaryKey()!.GetName()!)
            .ToList();

        // then - the current month and one month ahead, by the server's clock
        (
            await PostgreSqlTestDatabase.ScalarAsync<char>(
                fixture.ConnectionString,
                $"""SELECT relkind FROM pg_class WHERE oid = '"{schema}"."{_Table}"'::regclass;""",
                AbortToken
            )
        )
            .Should()
            .Be('p');
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"SELECT indexname::text FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{_Table}';",
                AbortToken
            )
        )
            .Should()
            .BeEquivalentTo(mapped);
        (await PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, schema, _Table, AbortToken))
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public async Task should_keep_and_drop_partitions_as_a_login_without_ddl_rights()
    {
        // given - the application clock is years ahead of the partitions the migration created
        const string schema = PartitionedAuditDbContext.Schema;
        await using var owner = await _MigrateAsOwnerAsync<PartitionedAuditDbContext>(schema);
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 5, 10, 0, 0, 0, TimeSpan.Zero));
        await using var app = EntityFrameworkAuditLogServices.Create<PartitionedAuditDbContext>(
            fixture.AppConnectionString,
            schema,
            retryOnFailure: true,
            options => options.PartitionMonthsAhead = 1,
            clock
        );
        var partitions = app.GetRequiredService<IAuditLogPartitions<PartitionedAuditDbContext>>();

        // when
        var created = await partitions.EnsureAsync(AbortToken);
        var again = await partitions.EnsureAsync(AbortToken);
        await EntityFrameworkAuditLogServices.AddAsync<PartitionedAuditDbContext>(
            app,
            AbortToken,
            ("may", clock.GetUtcNow().UtcDateTime)
        );
        var truncate = () =>
            PostgreSqlTestDatabase.ExecuteAsync(
                fixture.OwnerConnectionString,
                $"""TRUNCATE "{schema}"."audit_log_entries_2031_05";""",
                AbortToken
            );
        var outOfRange = () =>
            EntityFrameworkAuditLogServices.AddAsync<PartitionedAuditDbContext>(
                app,
                AbortToken,
                ("no.partition", new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            );
        await truncate.Should().ThrowAsync<PostgresException>();
        await outOfRange.Should().ThrowAsync<DbUpdateException>();
        var dropped = await partitions.DropExpiredAsync(TimeSpan.FromDays(365), AbortToken);

        // then - the two months the migration created ended more than a year before the application clock
        created.Should().Be(2);
        again.Should().Be(0);
        dropped.Should().Be(2);
        (await PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, schema, _Table, AbortToken))
            .Should()
            .Equal("audit_log_entries_2031_05", "audit_log_entries_2031_06");
        (await _ActionsAsync(schema)).Should().Equal("may");
    }

    [Fact]
    public async Task should_create_the_scaffolded_table_partitioned_with_the_columns_keys_and_indexes_the_model_maps()
    {
        // given, when - the migration holds only what EF Core scaffolds, then the helpers
        const string schema = ScaffoldedPartitionedAuditDbContext.Schema;
        await using var owner = await _MigrateAsOwnerAsync<ScaffoldedPartitionedAuditDbContext>(schema);
        var factory = owner.GetRequiredService<IDbContextFactory<ScaffoldedPartitionedAuditDbContext>>();
        await using var context = await factory.CreateDbContextAsync(AbortToken);
        var entity = context.Model.FindEntityType(typeof(AuditLogEntry))!;
        var table = StoreObjectIdentifier.Table(_Table, schema);
        var mappedIndexes = entity
            .GetIndexes()
            .Select(index => index.GetDatabaseName()!)
            .Append(entity.FindPrimaryKey()!.GetName()!)
            .ToList();
        var mappedColumns = entity.GetProperties().Select(property => property.GetColumnName(table)!).ToList();

        // then - partitioned, and nothing of the template is left
        (
            await PostgreSqlTestDatabase.ScalarAsync<char>(
                fixture.ConnectionString,
                $"""SELECT relkind FROM pg_class WHERE oid = '"{schema}"."{_Table}"'::regclass;""",
                AbortToken
            )
        )
            .Should()
            .Be('p');
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"""
                SELECT c.relname::text FROM pg_class AS c JOIN pg_namespace AS n ON n.oid = c.relnamespace
                WHERE n.nspname = '{schema}' AND c.relname LIKE '%template%'
                UNION ALL
                SELECT conname::text FROM pg_constraint
                WHERE conrelid = '"{schema}"."{_Table}"'::regclass AND conname LIKE '%template%';
                """,
                AbortToken
            )
        ).Should().BeEmpty();

        // and the model's columns, key, indexes, check constraint, identity, default, and comment are all there
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"""
                SELECT attname::text FROM pg_attribute
                WHERE attrelid = '"{schema}"."{_Table}"'::regclass AND attnum > 0 AND NOT attisdropped;
                """,
                AbortToken
            )
        )
            .Should()
            .BeEquivalentTo(mappedColumns);
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"SELECT indexname::text FROM pg_indexes WHERE schemaname = '{schema}' AND tablename = '{_Table}';",
                AbortToken
            )
        )
            .Should()
            .BeEquivalentTo(mappedIndexes);
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"""
                SELECT conname::text FROM pg_constraint
                WHERE conrelid = '"{schema}"."{_Table}"'::regclass AND contype = 'c';
                """,
                AbortToken
            )
        ).Should().Equal(ScaffoldedPartitionedAuditDbContext.CategoryCheck);
        (
            await PostgreSqlTestDatabase.ScalarAsync<string>(
                fixture.ConnectionString,
                $"""SELECT obj_description('"{schema}"."{_Table}"'::regclass, 'pg_class');""",
                AbortToken
            )
        )
            .Should()
            .Be(ScaffoldedPartitionedAuditDbContext.TableComment);
        (await PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, schema, _Table, AbortToken))
            .Should()
            .HaveCount(2);

        // and a row saved through EF Core gets its id from the copied identity and the copied column default
        await EntityFrameworkAuditLogServices.AddAsync<ScaffoldedPartitionedAuditDbContext>(
            owner,
            AbortToken,
            ("first", DateTime.UtcNow),
            ("second", DateTime.UtcNow)
        );
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"""SELECT "id"::text || ':' || "category" FROM "{schema}"."{_Table}" ORDER BY "id";""",
                AbortToken
            )
        )
            .Should()
            .Equal("1:data", "2:data");
    }

    [Fact]
    public async Task should_let_the_granted_login_keep_the_partitions_of_the_scaffolded_table()
    {
        // given - the migration granted the partition functions with the statement the framework builds
        const string schema = ScaffoldedPartitionedAuditDbContext.Schema;
        await using var owner = await _MigrateAsOwnerAsync<ScaffoldedPartitionedAuditDbContext>(schema);
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 5, 10, 0, 0, 0, TimeSpan.Zero));
        await using var app = EntityFrameworkAuditLogServices.Create<ScaffoldedPartitionedAuditDbContext>(
            fixture.AppConnectionString,
            schema,
            retryOnFailure: true,
            options => options.PartitionMonthsAhead = 1,
            clock
        );
        var partitions = app.GetRequiredService<IAuditLogPartitions<ScaffoldedPartitionedAuditDbContext>>();

        // when
        var created = await partitions.EnsureAsync(AbortToken);
        var dropped = await partitions.DropExpiredAsync(TimeSpan.FromDays(365), AbortToken);

        // then
        created.Should().Be(2);
        dropped.Should().Be(2);
        (await PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, schema, _Table, AbortToken))
            .Should()
            .Equal("audit_log_entries_2031_05", "audit_log_entries_2031_06");
    }

    [Fact]
    public async Task should_remove_the_partitioned_table_and_its_functions_when_the_migration_is_reverted()
    {
        // given
        const string schema = ScaffoldedPartitionedAuditDbContext.Schema;
        await using var owner = await _MigrateAsOwnerAsync<ScaffoldedPartitionedAuditDbContext>(schema);

        // when
        await EntityFrameworkAuditLogServices.MigrateAsync<ScaffoldedPartitionedAuditDbContext>(
            owner,
            Migration.InitialDatabase,
            AbortToken
        );

        // then - only the migrations history table is left in the schema
        (
            await PostgreSqlTestDatabase.ScalarAsync<long>(
                fixture.ConnectionString,
                $"""
                SELECT count(*) FROM pg_proc AS p JOIN pg_namespace AS n ON n.oid = p.pronamespace
                WHERE n.nspname = '{schema}';
                """,
                AbortToken
            )
        )
            .Should()
            .Be(0);
        (
            await PostgreSqlTestDatabase.ListAsync(
                fixture.ConnectionString,
                $"""
                SELECT c.relname::text FROM pg_class AS c JOIN pg_namespace AS n ON n.oid = c.relnamespace
                WHERE n.nspname = '{schema}' AND c.relkind IN ('r', 'p', 'S');
                """,
                AbortToken
            )
        ).Should().Equal(AuditLogMigrationScripts.HistoryTable);
    }

    [Fact]
    public async Task should_create_the_scaffolded_table_partitioned_from_an_idempotent_script()
    {
        // given - the script a deployment reviews and applies instead of running the migrator
        const string schema = ScaffoldedPartitionedAuditDbContext.Schema;
        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.ConnectionString,
            $"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""",
            AbortToken
        );
        await using var owner = EntityFrameworkAuditLogServices.Create<ScaffoldedPartitionedAuditDbContext>(
            fixture.OwnerConnectionString,
            schema,
            retryOnFailure: true
        );
        var factory = owner.GetRequiredService<IDbContextFactory<ScaffoldedPartitionedAuditDbContext>>();
        await using var context = await factory.CreateDbContextAsync(AbortToken);
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        // when - applied twice, as a deployment that re-runs the script would
        await PostgreSqlTestDatabase.ExecuteAsync(fixture.OwnerConnectionString, script, AbortToken);
        await PostgreSqlTestDatabase.ExecuteAsync(fixture.OwnerConnectionString, script, AbortToken);

        // then
        (
            await PostgreSqlTestDatabase.ScalarAsync<char>(
                fixture.ConnectionString,
                $"""SELECT relkind FROM pg_class WHERE oid = '"{schema}"."{_Table}"'::regclass;""",
                AbortToken
            )
        )
            .Should()
            .Be('p');
        (await PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, schema, _Table, AbortToken))
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public async Task should_refuse_a_table_that_was_not_created_partitioned()
    {
        // given - the plain table EF Core scaffolds, without the PARTITION BY edit
        const string schema = "audit_log_ef_not_partitioned";
        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.ConnectionString,
            $"""
            DROP SCHEMA IF EXISTS "{schema}" CASCADE;
            CREATE SCHEMA "{schema}";
            CREATE TABLE "{schema}"."{_Table}" ("id" bigint NOT NULL, "created_at" timestamp with time zone NOT NULL, PRIMARY KEY ("created_at", "id"));
            """,
            AbortToken
        );
        var migrationBuilder = new MigrationBuilder(HeadlessStorageNaming.NpgsqlEfProviderName);
        migrationBuilder.AddHeadlessAuditLogPartitions(schema: schema);
        var sql = ((SqlOperation)migrationBuilder.Operations[0]).Sql;

        // when
        var act = () => PostgreSqlTestDatabase.ExecuteAsync(fixture.ConnectionString, sql, AbortToken);

        // then - nothing was installed beside the plain table
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.ObjectNotInPrerequisiteState);
        (
            await PostgreSqlTestDatabase.ScalarAsync<long>(
                fixture.ConnectionString,
                $"""
                SELECT count(*) FROM pg_proc AS p JOIN pg_namespace AS n ON n.oid = p.pronamespace
                WHERE n.nspname = '{schema}';
                """,
                AbortToken
            )
        ).Should().Be(0);
    }

    private async Task<ServiceProvider> _MigrateAsOwnerAsync<TContext>(string schema)
        where TContext : DbContext
    {
        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.ConnectionString,
            $"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""",
            AbortToken
        );

        var services = EntityFrameworkAuditLogServices.Create<TContext>(
            fixture.OwnerConnectionString,
            schema,
            retryOnFailure: true
        );
        await EntityFrameworkAuditLogServices.MigrateAsync<TContext>(services, targetMigration: null, AbortToken);

        return services;
    }

    private Task<List<string>> _ActionsAsync(string schema)
    {
        return PostgreSqlTestDatabase.ListAsync(
            fixture.ConnectionString,
            $"""SELECT "action" FROM "{schema}"."{_Table}" ORDER BY "created_at", "id";""",
            AbortToken
        );
    }
}
