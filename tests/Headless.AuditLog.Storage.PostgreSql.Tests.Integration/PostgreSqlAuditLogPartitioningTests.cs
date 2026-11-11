// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogPartitioningTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_pg_partitions";
    private const string _Table = "audit_log_entries";

    // A fixed clock far from the server's own date: the partitions follow the application's clock, and no test can
    // straddle a real month boundary.
    private static readonly DateTimeOffset _Now = new(2031, 5, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_create_a_partitioned_table_with_the_current_month_and_the_months_ahead()
    {
        // given, when
        using var host = await _StartHostAsync(new FakeTimeProvider(_Now), monthsAhead: 2);

        // then
        (await _RelationKindAsync(_Table))
            .Should()
            .Be('p');
        (await _PartitionsAsync())
            .Should()
            .Equal("audit_log_entries_2031_05", "audit_log_entries_2031_06", "audit_log_entries_2031_07");
    }

    [Fact]
    public async Task should_create_only_the_missing_months_when_ensured_again()
    {
        // given
        var clock = new FakeTimeProvider(_Now);
        using var host = await _StartHostAsync(clock, monthsAhead: 2);
        var partitions = host.Services.GetRequiredService<IAuditLogPartitions<object>>();

        // when
        var sameMonth = await partitions.EnsureAsync(AbortToken);
        await host.Services.ProvisionResourcesAsync(AbortToken);
        var afterProvision = await _PartitionsAsync();
        clock.SetUtcNow(_Now.AddMonths(1));
        var nextMonth = await partitions.EnsureAsync(AbortToken);

        // then
        sameMonth.Should().Be(0);
        afterProvision.Should().HaveCount(3);
        nextMonth.Should().Be(1);
        (await _PartitionsAsync()).Should().HaveCount(4).And.Contain("audit_log_entries_2031_08");
    }

    [Fact]
    public async Task should_store_each_entry_in_the_partition_of_its_month()
    {
        // given
        using var host = await _StartHostAsync(new FakeTimeProvider(_Now), monthsAhead: 2);

        // when
        await _SaveAsync(host, _Now, "may");
        await _SaveAsync(host, new DateTimeOffset(2031, 6, 1, 0, 0, 0, TimeSpan.Zero), "june.first.instant");
        await _SaveAsync(host, new DateTimeOffset(2031, 6, 30, 23, 59, 59, TimeSpan.Zero), "june.last.second");

        // then
        (await _ActionsInAsync("audit_log_entries_2031_05"))
            .Should()
            .Equal("may");
        (await _ActionsInAsync("audit_log_entries_2031_06")).Should().Equal("june.first.instant", "june.last.second");
    }

    [Fact]
    public async Task should_refuse_an_entry_whose_month_has_no_partition()
    {
        // given
        using var host = await _StartHostAsync(new FakeTimeProvider(_Now), monthsAhead: 0);

        // when
        var act = async () => await _SaveAsync(host, _Now.AddMonths(1), "no.partition");

        // then - no default partition catches it
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.CheckViolation);
        (await _ActionsInAsync(_Table)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("""UPDATE "{0}"."audit_log_entries_2031_05" SET "action" = 'tampered';""")]
    [InlineData("""DELETE FROM "{0}"."audit_log_entries_2031_05";""")]
    [InlineData("""TRUNCATE "{0}"."audit_log_entries_2031_05";""")]
    [InlineData("""TRUNCATE "{0}"."audit_log_entries";""")]
    public async Task should_refuse_changes_made_straight_to_a_partition_when_append_only(string statement)
    {
        // given
        using var host = await _StartHostAsync(new FakeTimeProvider(_Now), monthsAhead: 0, appendOnly: true);
        await _SaveAsync(host, _Now, "entity.created");

        // when
        var act = () => _ExecuteAsync(string.Format(CultureInfo.InvariantCulture, statement, _Schema));

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _ActionsInAsync(_Table)).Should().Equal("entity.created");
    }

    [Fact]
    public async Task should_guard_a_partition_created_later_against_truncate()
    {
        // given
        var clock = new FakeTimeProvider(_Now);
        using var host = await _StartHostAsync(clock, monthsAhead: 0, appendOnly: true);
        clock.SetUtcNow(_Now.AddMonths(1));
        await host.Services.GetRequiredService<IAuditLogPartitions<object>>().EnsureAsync(AbortToken);
        await _SaveAsync(host, clock.GetUtcNow(), "june");

        // when
        var act = () => _ExecuteAsync($"""TRUNCATE "{_Schema}"."audit_log_entries_2031_06";""");

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _ActionsInAsync("audit_log_entries_2031_06")).Should().Equal("june");
    }

    [Fact]
    public async Task should_guard_the_partitions_that_exist_when_append_only_is_turned_on_later()
    {
        // given - a partitioned table that was provisioned without the trigger
        using (var first = await _StartHostAsync(new FakeTimeProvider(_Now), monthsAhead: 1))
        {
            await _SaveAsync(first, _Now, "written.before");
        }

        // when - the next release turns append-only on and provisions again
        using var host = await _StartHostAsync(
            new FakeTimeProvider(_Now),
            monthsAhead: 1,
            appendOnly: true,
            dropSchema: false
        );
        var act = () => _ExecuteAsync($"""TRUNCATE "{_Schema}"."audit_log_entries_2031_05";""");

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _ActionsInAsync(_Table)).Should().Equal("written.before");
    }

    [Fact]
    public async Task should_drop_only_the_months_that_ended_before_the_retention_period()
    {
        // given - January through March, then May through July
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 1, 10, 0, 0, 0, TimeSpan.Zero));
        using var host = await _StartHostAsync(clock, monthsAhead: 2, appendOnly: true);
        await _SaveAsync(host, clock.GetUtcNow(), "january");
        clock.SetUtcNow(_Now);
        var partitions = host.Services.GetRequiredService<IAuditLogPartitions<object>>();
        await partitions.EnsureAsync(AbortToken);
        await _SaveAsync(host, _Now, "may");

        // when - the cutoff is 11 March, inside March
        var dropped = await partitions.DropExpiredAsync(TimeSpan.FromDays(60), AbortToken);
        var again = await partitions.DropExpiredAsync(TimeSpan.FromDays(60), AbortToken);

        // then - March still holds days inside the retention period, so it stays whole
        dropped.Should().Be(2);
        again.Should().Be(0);
        (await _PartitionsAsync())
            .Should()
            .Equal(
                "audit_log_entries_2031_03",
                "audit_log_entries_2031_05",
                "audit_log_entries_2031_06",
                "audit_log_entries_2031_07"
            );
        (await _ActionsInAsync(_Table)).Should().Equal("may");
    }

    [Fact]
    public async Task should_refuse_to_drop_a_month_inside_the_minimum_retention()
    {
        // given - the server's own clock decides, and the application clock is years ahead of it
        using var host = await _StartHostAsync(
            new FakeTimeProvider(_Now),
            monthsAhead: 0,
            minimumRetention: TimeSpan.FromDays(30)
        );
        var partitions = host.Services.GetRequiredService<IAuditLogPartitions<object>>();

        // when
        var act = () => partitions.DropExpiredAsync(TimeSpan.FromDays(1), AbortToken);

        // then
        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.InsufficientPrivilege);
        (await _PartitionsAsync()).Should().Equal("audit_log_entries_2031_05");
    }

    [Fact]
    public async Task should_still_purge_expired_rows_through_the_retention_when_partitioned_and_append_only()
    {
        // given
        var clock = new FakeTimeProvider(_Now);
        using var host = await _StartHostAsync(clock, monthsAhead: 1, appendOnly: true);
        await _SaveAsync(host, _Now, "expired");
        await _SaveAsync(host, _Now.AddDays(25), "recent");
        clock.SetUtcNow(_Now.AddDays(30));

        // when
        var deleted = await host
            .Services.GetRequiredService<IAuditLogRetention<object>>()
            .PurgeAsync(TimeSpan.FromDays(10), limit: 10, AbortToken);

        // then
        deleted.Should().Be(1);
        (await _ActionsInAsync(_Table)).Should().Equal("recent");
    }

    [Fact]
    public async Task should_report_the_missing_month_and_gate_readiness_when_the_current_month_has_no_partition()
    {
        // given - provisioned in May, and nothing kept the partitions since
        using (await _StartHostAsync(new FakeTimeProvider(_Now), monthsAhead: 0)) { }

        var builder = _CreateBuilder(
            new FakeTimeProvider(_Now.AddMonths(3)),
            monthsAhead: 0,
            appendOnly: false,
            minimumRetention: null
        );
        using var host = builder.Build();
        var provisioner = (await _PartitionProvisionersAsync(host)).Should().ContainSingle().Subject;

        // when
        var verify = () => provisioner.VerifyAsync(AbortToken);
        var status = await provisioner.GetStatusAsync(AbortToken);

        // then
        (await verify.Should().ThrowAsync<ResourceProvisioningException>()).WithMessage(
            "*audit_log_entries_2031_08*resources provision*"
        );
        provisioner.StartupCheck.Should().Be(ResourceStartupCheck.GateReadiness);
        provisioner.CanPurge.Should().BeFalse();
        provisioner.CanDelete.Should().BeFalse();
        status
            .Should()
            .Equal(
                new KeyValuePair<string, string>("partitions", "1"),
                new KeyValuePair<string, string>("first", "audit_log_entries_2031_05"),
                new KeyValuePair<string, string>("last", "audit_log_entries_2031_05")
            );
    }

    [Fact]
    public async Task should_offer_no_partitions_resource_and_refuse_partition_calls_when_not_partitioned()
    {
        // given
        await _DropSchemaAsync();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UsePostgreSql(fixture.ConnectionString);
        });
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        var partitions = host.Services.GetRequiredService<IAuditLogPartitions<object>>();

        // when
        var ensure = () => partitions.EnsureAsync(AbortToken);
        var drop = () => partitions.DropExpiredAsync(TimeSpan.FromDays(1), AbortToken);

        // then
        (await _PartitionProvisionersAsync(host))
            .Should()
            .BeEmpty();
        (await _RelationKindAsync(_Table)).Should().Be('r');
        await ensure.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Partitioning*");
        await drop.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Partitioning*");
    }

    private async Task<IHost> _StartHostAsync(
        FakeTimeProvider clock,
        int monthsAhead,
        bool appendOnly = false,
        TimeSpan? minimumRetention = null,
        bool dropSchema = true
    )
    {
        if (dropSchema)
        {
            await _DropSchemaAsync();
        }

        var host = _CreateBuilder(clock, monthsAhead, appendOnly, minimumRetention).Build();
        await host.StartAsync(AbortToken);

        return host;
    }

    private HostApplicationBuilder _CreateBuilder(
        FakeTimeProvider clock,
        int monthsAhead,
        bool appendOnly,
        TimeSpan? minimumRetention
    )
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = _Schema;
                options.AppendOnly = appendOnly;
                options.Partitioning = AuditLogPartitioning.Monthly;
                options.PartitionMonthsAhead = monthsAhead;
                options.MinimumRetention = minimumRetention;
            });
            setup.UsePostgreSql(fixture.ConnectionString);
        });
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);

        return builder;
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

    private static async Task<List<IResourceProvisioner>> _PartitionProvisionersAsync(IHost host)
    {
        var provisioners = new List<IResourceProvisioner>();

        foreach (var source in host.Services.GetServices<IResourceProvisionerSource>())
        {
            provisioners.AddRange(
                (await source.GetProvisionersAsync(AbortToken)).Where(p =>
                    string.Equals(p.Kind, "AuditLogPartitions", StringComparison.Ordinal)
                )
            );
        }

        return provisioners;
    }

    private Task<List<string>> _PartitionsAsync()
    {
        return PostgreSqlTestDatabase.PartitionsAsync(fixture.ConnectionString, _Schema, _Table, AbortToken);
    }

    private Task<List<string>> _ActionsInAsync(string table)
    {
        return PostgreSqlTestDatabase.ListAsync(
            fixture.ConnectionString,
            $"""SELECT "action" FROM "{_Schema}"."{table}" ORDER BY "created_at", "id";""",
            AbortToken
        );
    }

    private Task<char> _RelationKindAsync(string table)
    {
        return PostgreSqlTestDatabase.ScalarAsync<char>(
            fixture.ConnectionString,
            $"""SELECT relkind FROM pg_class WHERE oid = '"{_Schema}"."{table}"'::regclass;""",
            AbortToken
        );
    }

    private Task _DropSchemaAsync()
    {
        return _ExecuteAsync($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""");
    }

    private Task _ExecuteAsync(string sql)
    {
        return PostgreSqlTestDatabase.ExecuteAsync(fixture.ConnectionString, sql, AbortToken);
    }
}
