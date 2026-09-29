// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

/// <summary>
/// An autonomous lease call that loses a deadlock waits a jittered delay on the store's clock before it retries in a
/// fresh transaction, and gives up with the provider's own error after three attempts.
/// </summary>
[Collection<PostgreSqlFencingFixture>]
public sealed class PostgreSqlLeaseDeadlockRetryTests(PostgreSqlFencingFixture fixture) : TestBase
{
    private const string _Schema = "fencing_pg_deadlock";

    [Fact]
    public async Task should_wait_on_the_clock_before_retrying_a_deadlocked_grant_then_succeed()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var host = await _CreateHostAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: 1, AbortToken);
        var store = host.Services.GetRequiredService<ILeaseStore>();

        // when
        var grant = store
            .GrantAsync(new LeaseKey("", "deadlock", Guid.NewGuid().ToString("N")), TimeSpan.FromMinutes(1), AbortToken)
            .AsTask();
        await injector.WaitForCountAsync(1, AbortToken);

        // then: with the clock frozen, the retry never starts
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
        grant.IsCompleted.Should().BeFalse();
        (await injector.CountAsync(AbortToken)).Should().Be(1);

        // and: once the clock moves past the delay, the retry runs in a fresh transaction and is granted
        await PostgreSqlDeadlockInjector.AdvanceUntilAsync(clock, () => Task.FromResult(grant.IsCompleted), AbortToken);
        (await grant).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_after_three_attempts_and_throw_the_deadlock_when_every_attempt_deadlocks()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var host = await _CreateHostAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: long.MaxValue, AbortToken);
        var store = host.Services.GetRequiredService<ILeaseStore>();

        // when
        var grant = store
            .GrantAsync(new LeaseKey("", "deadlock", Guid.NewGuid().ToString("N")), TimeSpan.FromMinutes(1), AbortToken)
            .AsTask();
        await PostgreSqlDeadlockInjector.AdvanceUntilAsync(clock, () => Task.FromResult(grant.IsCompleted), AbortToken);

        // then
        var thrown = await FluentActions.Awaiting(() => grant).Should().ThrowExactlyAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("40P01");
        (await injector.CountAsync(AbortToken)).Should().Be(3);
    }

    private async Task<LeasesHost> _CreateHostAsync(FakeTimeProvider clock)
    {
        await _Injector().DropSchemaAsync(AbortToken);

        return await fixture.CreateHostAsync(
            setup => setup.ConfigureStorage(storage => storage.Schema = _Schema),
            clock,
            AbortToken
        );
    }

    private PostgreSqlDeadlockInjector _Injector()
    {
        return new PostgreSqlDeadlockInjector(fixture.ConnectionString, _Schema, "fencing_leases");
    }
}
