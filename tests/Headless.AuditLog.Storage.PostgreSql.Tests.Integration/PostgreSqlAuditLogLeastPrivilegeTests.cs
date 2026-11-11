// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

/// <summary>
/// The least-privilege deployment: the owner login provisions, and the application login, which owns nothing and has
/// no DDL rights, runs the host.
/// </summary>
[Collection<PostgreSqlAuditLogRolesFixture>]
public sealed class PostgreSqlAuditLogLeastPrivilegeTests(PostgreSqlAuditLogRolesFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_pg_roles";
    private const string _Table = "audit_log_entries";
    private const string _App = PostgreSqlAuditLogRolesFixture.App;
    private static readonly DateTimeOffset _Expired = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_refuse_a_delete_by_a_login_that_sets_the_purge_setting_when_the_owner_holds_the_purge()
    {
        // given
        using var owner = await _ProvisionAsync(o => o.PurgeAuthority = AuditLogPurgeAuthority.Owner);
        await _SaveAsync(owner, _Expired, "expired");

        // when - what the retention itself runs under the session-setting authority
        var act = () => _ExecuteAsAppAsync(_SetPurgeSettingAndDelete);

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _ActionsAsync()).Should().Equal("expired");
    }

    [Fact]
    public async Task should_still_let_any_login_purge_with_the_setting_under_the_default_authority()
    {
        // given - the behavior before the owner authority existed stays the default
        using var owner = await _ProvisionAsync(_ => { });
        await _SaveAsync(owner, _Expired, "expired");

        // when
        await _ExecuteAsAppAsync(_SetPurgeSettingAndDelete);

        // then
        (await _ActionsAsync())
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task should_purge_through_the_owners_function_as_a_login_granted_execute()
    {
        // given
        using var owner = await _ProvisionAsync(o => o.PurgeAuthority = AuditLogPurgeAuthority.Owner);
        await _SaveAsync(owner, _Expired, "expired.first");
        await _SaveAsync(owner, _Expired.AddMinutes(1), "expired.second");
        await _SaveAsync(owner, DateTimeOffset.UtcNow, "recent");
        await _GrantExecuteAsync("audit_log_entries_purge");
        using var app = _BuildAppHost(o => o.PurgeAuthority = AuditLogPurgeAuthority.Owner);
        var retention = app.Services.GetRequiredService<IAuditLogRetention<object>>();

        // when
        var first = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 1, AbortToken);
        var second = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);
        var deleteAfterwards = () => _ExecuteAsAppAsync(_SetPurgeSettingAndDelete);

        // then
        first.Should().Be(1);
        second.Should().Be(1);
        await deleteAfterwards.Should().ThrowAsync<PostgresException>();
        (await _ActionsAsync()).Should().Equal("recent");
    }

    [Fact]
    public async Task should_refuse_the_purge_function_to_a_login_without_execute()
    {
        // given - the function is closed to PUBLIC, and nothing was granted
        using var owner = await _ProvisionAsync(o => o.PurgeAuthority = AuditLogPurgeAuthority.Owner);
        await _SaveAsync(owner, _Expired, "expired");
        using var app = _BuildAppHost(o => o.PurgeAuthority = AuditLogPurgeAuthority.Owner);
        var retention = app.Services.GetRequiredService<IAuditLogRetention<object>>();

        // when
        var act = () => retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _ActionsAsync()).Should().Equal("expired");
    }

    [Fact]
    public async Task should_refuse_a_purge_inside_the_minimum_retention_whatever_the_caller_asks()
    {
        // given
        using var owner = await _ProvisionAsync(o =>
        {
            o.PurgeAuthority = AuditLogPurgeAuthority.Owner;
            o.MinimumRetention = TimeSpan.FromDays(30);
        });
        await _SaveAsync(owner, DateTimeOffset.UtcNow.AddDays(-10), "ten.days.old");
        await _SaveAsync(owner, _Expired, "expired");
        await _GrantExecuteAsync("audit_log_entries_purge");
        using var app = _BuildAppHost(o =>
        {
            o.PurgeAuthority = AuditLogPurgeAuthority.Owner;
            o.MinimumRetention = TimeSpan.FromDays(30);
        });
        var retention = app.Services.GetRequiredService<IAuditLogRetention<object>>();

        // when
        var tooRecent = () => retention.PurgeAsync(TimeSpan.FromDays(1), limit: 10, AbortToken);
        var allowed = await retention.PurgeAsync(TimeSpan.FromDays(31), limit: 10, AbortToken);

        // then
        (await tooRecent.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        allowed.Should().Be(1);
        (await _ActionsAsync()).Should().Equal("ten.days.old");
    }

    [Fact]
    public async Task should_add_the_owner_check_to_a_table_that_is_already_append_only()
    {
        // given - a release that provisioned the append-only trigger under the default authority
        using (var before = await _ProvisionAsync(_ => { }))
        {
            await _SaveAsync(before, _Expired, "expired");
        }

        // when - the next release moves the purge to the owner
        using var owner = await _ProvisionAsync(
            o => o.PurgeAuthority = AuditLogPurgeAuthority.Owner,
            dropSchema: false
        );
        var act = () => _ExecuteAsAppAsync(_SetPurgeSettingAndDelete);

        // then - the recorded trigger step is unchanged, so the start passed, and the new step closed the setting
        await act.Should().ThrowAsync<PostgresException>();
        (await _ActionsAsync()).Should().Equal("expired");
    }

    [Fact]
    public async Task should_create_partitions_for_a_login_without_ddl_rights_only_through_the_function()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 5, 10, 0, 0, 0, TimeSpan.Zero));
        using var owner = await _ProvisionAsync(_MonthlyWithNoMonthsAhead, clock);
        using var app = _BuildAppHost(_MonthlyWithNoMonthsAhead, clock);
        var partitions = app.Services.GetRequiredService<IAuditLogPartitions<object>>();
        clock.SetUtcNow(new DateTimeOffset(2031, 9, 1, 0, 0, 0, TimeSpan.Zero));

        // when
        var byHand = () =>
            _ExecuteAsAppAsync(
                $"""CREATE TABLE "{_Schema}"."by_hand" PARTITION OF "{_Schema}"."{_Table}" FOR VALUES FROM ('2031-09-01') TO ('2031-10-01');"""
            );
        var withoutExecute = () => partitions.EnsureAsync(AbortToken);
        await byHand.Should().ThrowAsync<PostgresException>();
        (await withoutExecute.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        await _GrantExecuteAsync("audit_log_entries_ensure_partitions");
        var created = await partitions.EnsureAsync(AbortToken);

        // then - the partition belongs to the owner, and the application login can write to it
        created.Should().Be(1);
        (await _OwnerOfAsync("audit_log_entries_2031_09")).Should().Be(PostgreSqlAuditLogRolesFixture.Owner);
        await _SaveAsync(app, clock.GetUtcNow(), "september");
        (await _ActionsAsync()).Should().Equal("september");
    }

    [Fact]
    public async Task should_drop_expired_partitions_for_a_login_granted_execute_and_guard_the_rest()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 1, 10, 0, 0, 0, TimeSpan.Zero));
        using var owner = await _ProvisionAsync(
            o =>
            {
                o.Partitioning = AuditLogPartitioning.Monthly;
                o.PartitionMonthsAhead = 1;
            },
            clock
        );
        await _SaveAsync(owner, clock.GetUtcNow(), "january");
        using var app = _BuildAppHost(
            o =>
            {
                o.Partitioning = AuditLogPartitioning.Monthly;
                o.PartitionMonthsAhead = 1;
            },
            clock
        );
        var partitions = app.Services.GetRequiredService<IAuditLogPartitions<object>>();
        clock.SetUtcNow(new DateTimeOffset(2031, 2, 15, 0, 0, 0, TimeSpan.Zero));

        // when
        var withoutExecute = () => partitions.DropExpiredAsync(TimeSpan.FromDays(10), AbortToken);
        await withoutExecute.Should().ThrowAsync<PostgresException>();
        await _GrantExecuteAsync("audit_log_entries_drop_partitions");
        var dropped = await partitions.DropExpiredAsync(TimeSpan.FromDays(10), AbortToken);
        var truncate = () => _ExecuteAsAppAsync($"""TRUNCATE "{_Schema}"."audit_log_entries_2031_02";""");

        // then - the login can truncate nothing itself: it does not own the partition
        dropped.Should().Be(1);
        (await PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, _Schema, _Table, AbortToken))
            .Should()
            .Equal("audit_log_entries_2031_02");
        await truncate.Should().ThrowAsync<PostgresException>();
    }

    private static void _MonthlyWithNoMonthsAhead(AuditLogStorageOptions options)
    {
        options.Partitioning = AuditLogPartitioning.Monthly;
        options.PartitionMonthsAhead = 0;
    }

    private const string _SetPurgeSettingAndDelete = $"""
        BEGIN;
        SELECT pg_catalog.set_config('headless.audit_log_purge', 'on', true);
        DELETE FROM "{_Schema}"."{_Table}";
        COMMIT;
        """;

    /// <summary>
    /// Provisions as the owner login with an append-only table, then grants the application login what a deployment
    /// grants it: the schema, and reading, writing, and deleting rows. Deleting is granted so that only the trigger
    /// stands between that login and the rows.
    /// </summary>
    private async Task<IHost> _ProvisionAsync(
        Action<AuditLogStorageOptions> configure,
        TimeProvider? clock = null,
        bool dropSchema = true
    )
    {
        if (dropSchema)
        {
            await PostgreSqlTestDatabase.ExecuteAsync(
                fixture.ConnectionString,
                $"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""",
                AbortToken
            );
        }

        var builder = _CreateBuilder(fixture.OwnerConnectionString, configure, clock);
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        var host = builder.Build();
        await host.StartAsync(AbortToken);

        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.OwnerConnectionString,
            $"""
            GRANT USAGE ON SCHEMA "{_Schema}" TO "{_App}";
            GRANT SELECT, INSERT, DELETE ON "{_Schema}"."{_Table}" TO "{_App}";
            """,
            AbortToken
        );

        return host;
    }

    /// <summary>Builds, without starting, the host the application login runs: it provisions nothing.</summary>
    private IHost _BuildAppHost(Action<AuditLogStorageOptions> configure, TimeProvider? clock = null)
    {
        return _CreateBuilder(fixture.AppConnectionString, configure, clock).Build();
    }

    private static HostApplicationBuilder _CreateBuilder(
        string connectionString,
        Action<AuditLogStorageOptions> configure,
        TimeProvider? clock
    )
    {
        var builder = Host.CreateApplicationBuilder();

        if (clock is not null)
        {
            builder.Services.AddSingleton(clock);
        }

        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = _Schema;
                options.AppendOnly = true;
                configure(options);
            });
            setup.UsePostgreSql(connectionString);
        });

        return builder;
    }

    private Task _GrantExecuteAsync(string function)
    {
        return PostgreSqlTestDatabase.ExecuteAsync(
            fixture.OwnerConnectionString,
            $"""GRANT EXECUTE ON FUNCTION "{_Schema}"."{function}" TO "{_App}";""",
            AbortToken
        );
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

    private Task _ExecuteAsAppAsync(string sql)
    {
        return PostgreSqlTestDatabase.ExecuteAsync(fixture.AppConnectionString, sql, AbortToken);
    }

    private Task<List<string>> _ActionsAsync()
    {
        return PostgreSqlTestDatabase.ListAsync(
            fixture.ConnectionString,
            $"""SELECT "action" FROM "{_Schema}"."{_Table}" ORDER BY "created_at", "id";""",
            AbortToken
        );
    }

    private Task<string> _OwnerOfAsync(string table)
    {
        return PostgreSqlTestDatabase.ScalarAsync<string>(
            fixture.ConnectionString,
            $"""SELECT pg_get_userbyid(relowner)::text FROM pg_class WHERE oid = '"{_Schema}"."{table}"'::regclass;""",
            AbortToken
        );
    }
}
