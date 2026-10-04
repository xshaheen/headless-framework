// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogFailureModesTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast.
        // Credentials are placeholders; we never reach the auth handshake because the TCP connect fails first.
        const string unreachable =
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=placeholder-never-used;Timeout=2";
        using var host = _CreateHost(unreachable);

        // when & then — wrapped in HostFailedToStartException by the host pipeline; inner is NpgsqlException
        await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is NpgsqlException || e.InnerException is NpgsqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();

        // The schema runner names the features whose connection failed and keeps the driver error as the cause.
        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(AbortToken))
            .Should()
            .ThrowAsync<SchemaRunnerException>()
            .WithInnerException(typeof(NpgsqlException));
    }

    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_authentication_fails()
    {
        // given — point at the real fixture but with a wrong password
        var badAuth = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Password = "wrong-password",
        }.ToString();
        using var host = _CreateHost(badAuth);

        // when & then
        var startThrew = await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<Exception>();
        startThrew
            .Which.Should()
            .Match<Exception>(e => e is PostgresException || e.InnerException is PostgresException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task should_succeed_when_multiple_hosts_initialize_concurrently_against_same_schema()
    {
        // given — 5 hosts racing to create the same schema/table; the initializer is
        // designed to be idempotent via CREATE IF NOT EXISTS + duplicate-error suppression.
        await _DropSchemaAsync("audit_log_pg_concurrent");
        const int hostCount = 5;
        var hosts = Enumerable
            .Range(0, hostCount)
            .Select(_ => _CreateHost(fixture.ConnectionString, "audit_log_pg_concurrent"))
            .ToArray();

        try
        {
            // when — start all hosts in parallel
            var startTasks = hosts.Select(h => h.StartAsync(AbortToken)).ToArray();
            await Task.WhenAll(startTasks);

            // then — all initializers report ready, exactly one audit_log_entries table exists, and the
            // full 6-index complement is present (regression guard: a swallowed CREATE INDEX
            // failure would otherwise pass the table-count assertion silently).
            hosts
                .Select(h => h.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized)
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            (await _CountTablesAsync("audit_log_pg_concurrent", "audit_log_entries")).Should().Be(1);
            (await _CountIndexesAsync("audit_log_pg_concurrent", "audit_log_entries")).Should().Be(6);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    [Fact]
    public async Task should_create_a_full_index_set_for_each_table_sharing_a_schema()
    {
        // given — PostgreSQL index names are unique per schema, so two audit tables in one schema only
        // both get their indexes when the index names are derived from the table name.
        const string schema = "audit_log_pg_two_tables";
        await _DropSchemaAsync(schema);
        using var primary = _CreateHost(fixture.ConnectionString, schema);
        using var archive = _CreateHost(fixture.ConnectionString, schema, "audit_log_archive");

        // when
        await primary.StartAsync(AbortToken);
        await archive.StartAsync(AbortToken);

        // then
        (await _CountTablesAsync(schema, "audit_log_entries"))
            .Should()
            .Be(1);
        (await _CountTablesAsync(schema, "audit_log_archive")).Should().Be(1);
        (await _CountIndexesAsync(schema, "audit_log_entries")).Should().Be(6);
        (await _CountIndexesAsync(schema, "audit_log_archive")).Should().Be(6);
    }

    [Fact]
    public void should_reject_a_table_name_whose_derived_index_names_exceed_the_identifier_limit()
    {
        // given — "ix_<table>_tenant_account_time" is the longest derived name; 41 characters push it to 64.
        using var host = _CreateHost(fixture.ConnectionString, tableName: new string('a', 41));
        var options = host.Services.GetRequiredService<IOptions<AuditLogStorageOptions>>();

        // when & then
        options
            .Invoking(x => x.Value)
            .Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should()
            .Contain(failure => failure.Contains("63", StringComparison.Ordinal));
    }

    [Fact]
    public void should_accept_a_table_name_at_the_derived_index_name_limit()
    {
        // given
        using var host = _CreateHost(fixture.ConnectionString, tableName: new string('a', 40));

        // when
        var options = host.Services.GetRequiredService<IOptions<AuditLogStorageOptions>>().Value;

        // then
        options.TableName.Should().HaveLength(40);
    }

    private static IHost _CreateHost(
        string connectionString,
        string schema = "audit_log_pg_failure",
        string? tableName = null
    )
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;
                options.TableName = tableName;
            });
            setup.UsePostgreSql(connectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<int> _CountTablesAsync(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue(nameof(table), table);

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    private async Task<int> _CountIndexesAsync(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        // Matches the 6 `CREATE INDEX IF NOT EXISTS ix_<table>_*` statements in the PG
        // initializer; the LIKE filter excludes the PK index (named `pk_<table>`).
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = @schema AND tablename = @table AND indexname LIKE 'ix_' || @table || '_%'
            """,
            connection
        );
        command.Parameters.AddWithValue(nameof(schema), schema);
        command.Parameters.AddWithValue(nameof(table), table);

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }
}
