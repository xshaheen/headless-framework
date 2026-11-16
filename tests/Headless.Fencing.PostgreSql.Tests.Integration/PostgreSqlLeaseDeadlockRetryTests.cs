// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
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
        var clock = new TimerSignalingFakeTimeProvider();
        await using var host = await _CreateHostAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: 1, AbortToken);
        var store = host.Services.GetRequiredService<ILeaseStore>();

        // when
        var grant = store
            .GrantAsync(
                new LeaseKey("", "deadlock", Guid.NewGuid().ToString("N")),
                TimeSpan.FromMinutes(1),
                new LeaseGrantOptions(),
                AbortToken
            )
            .AsTask();

        // then: the failed attempt armed its retry delay on the clock, and with the clock frozen the retry cannot start
        await clock.WhenTimersArmed(1).Bounded();
        grant.IsCompleted.Should().BeFalse();
        (await injector.CountAsync(AbortToken)).Should().Be(1);

        // and: once the clock moves past the delay, the retry runs in a fresh transaction and is granted
        clock.Advance(TimeSpan.FromMinutes(1));
        (await grant.Bounded()).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_stop_after_three_attempts_and_throw_the_deadlock_when_every_attempt_deadlocks()
    {
        // given
        var clock = new TimerSignalingFakeTimeProvider();
        await using var host = await _CreateHostAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: long.MaxValue, AbortToken);
        var store = host.Services.GetRequiredService<ILeaseStore>();

        // when
        var grant = store
            .GrantAsync(
                new LeaseKey("", "deadlock", Guid.NewGuid().ToString("N")),
                TimeSpan.FromMinutes(1),
                new LeaseGrantOptions(),
                AbortToken
            )
            .AsTask();

        // Each retry waits on the clock after its attempt fails, so the third and last attempt starts only once the
        // second delay has been armed and fired.
        for (var retry = 1; retry <= 2; retry++)
        {
            await clock.WhenTimersArmed(retry).Bounded();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        // then
        var thrown = await FluentActions
            .Awaiting(() => grant.Bounded())
            .Should()
            .ThrowExactlyAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("40P01");
        (await injector.CountAsync(AbortToken)).Should().Be(3);
    }

    private async Task<LeasesHost> _CreateHostAsync(TimerSignalingFakeTimeProvider clock)
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
