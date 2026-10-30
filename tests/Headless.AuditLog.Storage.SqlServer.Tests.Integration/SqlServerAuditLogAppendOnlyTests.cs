// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerAuditLogAppendOnlyTests(SqlServerAuditLogFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_sql_append_only";
    private static readonly DateTimeOffset _Expired = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("UPDATE [{0}].[AuditLogEntries] SET [Action] = N'tampered';")]
    [InlineData("DELETE FROM [{0}].[AuditLogEntries];")]
    public async Task should_refuse_changes_to_written_rows_when_append_only(string statement)
    {
        // given
        using var host = await _StartHostAsync(appendOnly: true);
        await _SaveAsync(host, _Expired, "entity.created");

        // when
        var act = () => _ExecuteAsync(string.Format(CultureInfo.InvariantCulture, statement, _Schema));

        // then
        (await act.Should().ThrowAsync<SqlException>())
            .Which.Number.Should()
            .Be(50000);
        (await _CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_purge_only_expired_rows_oldest_first_up_to_the_limit_when_append_only()
    {
        // given
        using var host = await _StartHostAsync(appendOnly: true);
        await _SaveAsync(host, _Expired, "expired.first");
        await _SaveAsync(host, _Expired.AddMinutes(1), "expired.second");
        await _SaveAsync(host, DateTimeOffset.UtcNow, "recent");
        var retention = host.Services.GetRequiredService<IAuditLogRetention<object>>();

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
    public async Task should_keep_refusing_deletes_after_a_purge_clears_the_session_key()
    {
        // given
        using var host = await _StartHostAsync(appendOnly: true);
        await _SaveAsync(host, _Expired, "expired");
        await _SaveAsync(host, DateTimeOffset.UtcNow, "recent");
        await host
            .Services.GetRequiredService<IAuditLogRetention<object>>()
            .PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);

        // when
        var act = () => _ExecuteAsync($"DELETE FROM [{_Schema}].[AuditLogEntries];");

        // then
        await act.Should().ThrowAsync<SqlException>();
        (await _ActionsAsync()).Should().Equal("recent");
    }

    [Fact]
    public async Task should_create_no_trigger_and_still_purge_when_not_append_only()
    {
        // given
        using var host = await _StartHostAsync(appendOnly: false);
        await _SaveAsync(host, _Expired, "expired");

        // when
        var deleted = await host
            .Services.GetRequiredService<IAuditLogRetention<object>>()
            .PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);
        await _SaveAsync(host, _Expired, "deletable");
        await _ExecuteAsync($"DELETE FROM [{_Schema}].[AuditLogEntries];");

        // then
        deleted.Should().Be(1);
        (await _CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_reject_a_non_positive_retention_or_limit()
    {
        // given
        using var host = await _StartHostAsync(appendOnly: false);
        var retention = host.Services.GetRequiredService<IAuditLogRetention<object>>();

        // when
        var zeroAge = () => retention.PurgeAsync(TimeSpan.Zero, limit: 1, AbortToken);
        var zeroLimit = () => retention.PurgeAsync(TimeSpan.FromDays(1), limit: 0, AbortToken);

        // then
        await zeroAge.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private async Task<IHost> _StartHostAsync(bool appendOnly)
    {
        await _ExecuteAsync(
            $"""
            IF OBJECT_ID(N'{_Schema}.AuditLogEntries', N'U') IS NOT NULL DROP TABLE [{_Schema}].[AuditLogEntries];
            IF OBJECT_ID(N'{_Schema}.headless_schema_history', N'U') IS NOT NULL DROP TABLE [{_Schema}].[headless_schema_history];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
            """
        );

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = _Schema;
                options.AppendOnly = appendOnly;
            });
            setup.UseSqlServer(fixture.ConnectionString);
        });

        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        var host = builder.Build();
        await host.StartAsync(AbortToken);

        return host;
    }

    private static async Task _SaveAsync(IHost host, DateTimeOffset createdAt, string action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();

        await store.SaveAsync(
            [new AuditLogEntryData { Action = action, CreatedAt = createdAt }],
            savingContext: new object(),
            AbortToken
        );
    }

    private async Task<int> _CountAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM [{_Schema}].[AuditLogEntries];", connection);

        return (int)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<List<string>> _ActionsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"SELECT [Action] FROM [{_Schema}].[AuditLogEntries] ORDER BY [CreatedAt], [Id];",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var actions = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            actions.Add(reader.GetString(0));
        }

        return actions;
    }

    private async Task _ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
