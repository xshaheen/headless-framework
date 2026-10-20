// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerAuditLogFailureModesTests(SqlServerAuditLogFixture fixture) : TestBase
{
    [Fact]
    public async Task should_fail_the_start_with_the_driver_error_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast.
        // Password is a placeholder; we never reach the auth handshake because the TCP connect fails first.
        const string unreachable =
            "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=true";
        using var host = _CreateHost(unreachable, setupResources: false);

        // The startup check fails the start; the schema runner names the features whose connection failed and keeps
        // the driver error as the cause.
        (
            await FluentActions
                .Awaiting(() => host.StartAsync(AbortToken))
                .Should()
                .ThrowAsync<ResourceProvisioningException>()
        )
            .WithInnerException<SchemaRunnerException>()
            .WithInnerException<SqlException>();
    }

    [Fact]
    public async Task should_fail_the_start_with_the_driver_error_when_authentication_fails()
    {
        // given — point at the real fixture but with a wrong password
        var badAuth = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Password = "wrong-password",
        }.ToString();
        using var host = _CreateHost(badAuth, setupResources: false);

        // when / then
        (
            await FluentActions
                .Awaiting(() => host.StartAsync(AbortToken))
                .Should()
                .ThrowAsync<ResourceProvisioningException>()
        )
            .WithInnerException<SchemaRunnerException>()
            .WithInnerException<SqlException>();
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
            var startTasks = hosts.Select(h => h.StartAsync(AbortToken)).ToArray();
            await Task.WhenAll(startTasks);

            // then — every host started, so each verified its schema; exactly one AuditLogEntries table exists, and the
            // full 6-index complement is present (regression guard: a CATCH that swallows a real
            // CREATE INDEX failure would otherwise pass the table-count assertion silently).
            (await _CountTablesAsync("audit_log_sql_concurrent", "AuditLogEntries"))
                .Should()
                .Be(1);
            (await _CountIndexesAsync("audit_log_sql_concurrent", "AuditLogEntries")).Should().Be(6);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private static IHost _CreateHost(
        string connectionString,
        string schema = "audit_log_sql_failure",
        bool setupResources = true
    )
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = schema);
            setup.UseSqlServer(connectionString);
        });

        if (setupResources)
        {
            builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        }

        return builder.Build();
    }

    private async Task _DropSchemaAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{schema}.AuditLogEntries', N'U') IS NOT NULL DROP TABLE [{schema}].[AuditLogEntries];
            IF OBJECT_ID(N'{schema}.headless_schema_history', N'U') IS NOT NULL DROP TABLE [{schema}].[headless_schema_history];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{schema}') EXEC(N'DROP SCHEMA [{schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<int> _CountTablesAsync(string schema, string table)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
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

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }

    private async Task<int> _CountIndexesAsync(string schema, string table)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        // Nonclustered indexes only (type = 2) — excludes the clustered PK so the count matches
        // the 6 CREATE NONCLUSTERED INDEX statements in SqlServerAuditLogSchemaContribution.
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{schema}].[{table}]') AND type = 2;",
            connection
        );

        return Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
    }
}
