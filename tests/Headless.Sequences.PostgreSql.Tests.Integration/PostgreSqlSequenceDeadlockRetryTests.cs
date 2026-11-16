// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

/// <summary>
/// An autonomous increment that loses a deadlock waits a jittered delay on the store's clock before it retries in a
/// fresh transaction, and gives up with the provider's own error after three attempts.
/// </summary>
[Collection<PostgreSqlSequencesFixture>]
public sealed class PostgreSqlSequenceDeadlockRetryTests(PostgreSqlSequencesFixture fixture) : TestBase
{
    private const string _Schema = "sequences_pg_deadlock";

    [Fact]
    public async Task should_wait_on_the_clock_before_retrying_a_deadlocked_increment_then_succeed()
    {
        // given
        var clock = new TimerArmedFakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: 1, AbortToken);
        var store = services.GetRequiredService<ISequenceStore>();

        // when: the victim arms its back-off timer after the failed attempt reached the table
        var increment = store.IncrementAsync(new SequenceKey("", "deadlock", ""), 1, 1, AbortToken).AsTask();
        var armed = clock.WaitForArmedTimerAsync(AbortToken);
        var first = await Task.WhenAny(armed, increment).Bounded();

        // then: with the clock frozen after that, the retry never starts
        first.Should().BeSameAs(armed, "the deadlock victim waits on the clock instead of retrying at once");
        increment.IsCompleted.Should().BeFalse();
        (await injector.CountAsync(AbortToken)).Should().Be(1);

        // and: once the clock moves past the delay, the retry runs in a fresh transaction and writes the first value
        clock.Advance(await armed);
        (await increment.Bounded()).Should().Be(1);
    }

    [Fact]
    public async Task should_stop_after_three_attempts_and_throw_the_deadlock_when_every_attempt_deadlocks()
    {
        // given
        var clock = new TimerArmedFakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: long.MaxValue, AbortToken);
        var store = services.GetRequiredService<ISequenceStore>();

        // when
        var increment = store.IncrementAsync(new SequenceKey("", "deadlock", ""), 1, 1, AbortToken).AsTask();
        await clock.AdvanceWhenArmedAsync(increment, AbortToken);

        // then
        var thrown = await FluentActions.Awaiting(() => increment).Should().ThrowExactlyAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("40P01");
        (await injector.CountAsync(AbortToken)).Should().Be(3);
    }

    private async Task<ServiceProvider> _CreateServicesAsync(TimerArmedFakeTimeProvider clock)
    {
        await _Injector().DropSchemaAsync(AbortToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        fixture.ConfigureUnitOfWork(services);
        services.AddHeadlessSequences(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.Schema = _Schema;
            })
        );

        // A host start only verifies the schema, so the test host creates it first, as a development host does.
        services.AddDeployStepsOnStartup(o => o.Seed = false);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        return provider;
    }

    private PostgreSqlDeadlockInjector _Injector()
    {
        return new PostgreSqlDeadlockInjector(fixture.ConnectionString, _Schema, "sequences");
    }
}
