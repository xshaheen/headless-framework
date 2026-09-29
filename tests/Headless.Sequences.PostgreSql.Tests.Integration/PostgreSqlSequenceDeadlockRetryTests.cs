// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
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
        var clock = new FakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: 1, AbortToken);
        var store = services.GetRequiredService<ISequenceStore>();

        // when
        var increment = store.IncrementAsync(new SequenceKey("", "deadlock", ""), 1, 1, AbortToken).AsTask();
        await injector.WaitForCountAsync(1, AbortToken);

        // then: with the clock frozen, the retry never starts
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
        increment.IsCompleted.Should().BeFalse();
        (await injector.CountAsync(AbortToken)).Should().Be(1);

        // and: once the clock moves past the delay, the retry runs in a fresh transaction and writes the first value
        await PostgreSqlDeadlockInjector.AdvanceUntilAsync(
            clock,
            () => Task.FromResult(increment.IsCompleted),
            AbortToken
        );
        (await increment).Should().Be(1);
    }

    [Fact]
    public async Task should_stop_after_three_attempts_and_throw_the_deadlock_when_every_attempt_deadlocks()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: long.MaxValue, AbortToken);
        var store = services.GetRequiredService<ISequenceStore>();

        // when
        var increment = store.IncrementAsync(new SequenceKey("", "deadlock", ""), 1, 1, AbortToken).AsTask();
        await PostgreSqlDeadlockInjector.AdvanceUntilAsync(
            clock,
            () => Task.FromResult(increment.IsCompleted),
            AbortToken
        );

        // then
        var thrown = await FluentActions.Awaiting(() => increment).Should().ThrowExactlyAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("40P01");
        (await injector.CountAsync(AbortToken)).Should().Be(3);
    }

    private async Task<ServiceProvider> _CreateServicesAsync(FakeTimeProvider clock)
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
