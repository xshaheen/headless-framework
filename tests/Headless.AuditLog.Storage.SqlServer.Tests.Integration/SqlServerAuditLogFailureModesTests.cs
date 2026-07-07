// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.AuditLog.SqlServer;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerAuditLogFailureModesTests(SqlServerAuditLogFixture fixture)
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast.
        // Password is a placeholder; we never reach the auth handshake because the TCP connect fails first.
        const string unreachable =
            "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=true";
        using var host = _CreateHost(unreachable);

        // when / then — wrapped in HostFailedToStartException by the host pipeline; inner is SqlException
        await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>()
            .Where(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<SqlException>();
    }

    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_authentication_fails()
    {
        // given — point at the real fixture but with a wrong password
        var badAuth = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Password = "wrong-password",
        }.ToString();
        using var host = _CreateHost(badAuth);

        // when / then
        var startThrew = await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>();
        startThrew.Which.Should().Match<Exception>(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task should_succeed_when_multiple_hosts_initialize_concurrently_against_same_schema()
    {
        // given — 5 hosts racing to create the same schema/table; the initializer is
        // designed to be idempotent via OBJECT_ID checks + duplicate-error suppression.
        await _DropSchemaAsync("audit_log_sql_concurrent");
        const int hostCount = 5;
        var hosts = Enumerable
            .Range(0, hostCount)
            .Select(_ => _CreateHost(fixture.ConnectionString, "audit_log_sql_concurrent"))
            .ToArray();

        try
        {
            // when — start all hosts in parallel
            var startTasks = hosts.Select(h => h.StartAsync(TestContext.Current.CancellationToken)).ToArray();
            await Task.WhenAll(startTasks);

            // then — all initializers report ready, exactly one audit_log table exists, and the
            // full 5-index complement is present (regression guard: a CATCH that swallows a real
            // CREATE INDEX failure would otherwise pass the table-count assertion silently).
            hosts
                .Select(h => h.Services.GetRequiredService<IEnumerable<IInitializer>>().Single().IsInitialized)
                .Should()
                .AllSatisfy(initialized => initialized.Should().BeTrue());
            (await _CountTablesAsync("audit_log_sql_concurrent", "audit_log")).Should().Be(1);
            (await _CountIndexesAsync("audit_log_sql_concurrent", "audit_log")).Should().Be(5);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private static IHost _CreateHost(string connectionString, string schema = "audit_log_sql_failure")
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = schema);
            setup.UseSqlServer(connectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{schema}.audit_log', N'U') IS NOT NULL DROP TABLE [{schema}].[audit_log];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{schema}') EXEC(N'DROP SCHEMA [{schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> _CountTablesAsync(string schema, string table)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private async Task<int> _CountIndexesAsync(string schema, string table)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        // Nonclustered indexes only (type = 2) — excludes the clustered PK so the count matches
        // the 5 CREATE NONCLUSTERED INDEX statements in SqlServerAuditLogStorageInitializer.
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{schema}].[{table}]') AND type = 2;",
            connection
        );

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            CultureInfo.InvariantCulture
        );
    }
}
