// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

/// <summary>
/// An autonomous record call that loses a deadlock waits a jittered delay on the store's clock before it retries in a
/// fresh transaction, and gives up with the provider's own error after three attempts.
/// </summary>
[Collection<PostgreSqlIdempotencyFixture>]
public sealed class PostgreSqlIdempotencyDeadlockRetryTests(PostgreSqlIdempotencyFixture fixture) : TestBase
{
    private const string _Schema = "idempotency_pg_deadlock";

    [Fact]
    public async Task should_wait_on_the_clock_before_retrying_a_deadlocked_purge_then_succeed()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: 1, AbortToken);
        var store = services.GetRequiredService<IIdempotencyRecordStore>();

        // when
        var purge = store.PurgeAsync(TimeSpan.FromDays(1), 100, AbortToken).AsTask();
        await injector.WaitForCountAsync(1, AbortToken);

        // then: with the clock frozen, the retry never starts
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
        purge.IsCompleted.Should().BeFalse();
        (await injector.CountAsync(AbortToken)).Should().Be(1);

        // and: once the clock moves past the delay, the retry runs in a fresh transaction and completes
        await PostgreSqlDeadlockInjector.AdvanceUntilAsync(clock, () => Task.FromResult(purge.IsCompleted), AbortToken);
        (await purge).Should().Be(0);
        (await injector.CountAsync(AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_stop_after_three_attempts_and_throw_the_deadlock_when_every_attempt_deadlocks()
    {
        // given
        var clock = new FakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var injector = _Injector();
        await injector.ArmAsync(failures: long.MaxValue, AbortToken);
        var store = services.GetRequiredService<IIdempotencyRecordStore>();

        // when
        var purge = store.PurgeAsync(TimeSpan.FromDays(1), 100, AbortToken).AsTask();
        await PostgreSqlDeadlockInjector.AdvanceUntilAsync(clock, () => Task.FromResult(purge.IsCompleted), AbortToken);

        // then
        var thrown = await FluentActions.Awaiting(() => purge).Should().ThrowExactlyAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be("40P01");
        (await injector.CountAsync(AbortToken)).Should().Be(3);
    }

    private async Task<ServiceProvider> _CreateServicesAsync(FakeTimeProvider clock)
    {
        await _Injector().DropSchemaAsync(AbortToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHeadlessIdempotency(setup =>
        {
            fixture.ConfigureIdempotency(setup);
            setup.ConfigureStorage(storage => storage.Schema = _Schema);
            // The retention sweep would purge on its own schedule and consume injected failures meant for the test.
            setup.ConfigureOptions(options => options.PurgeInterval = null);
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        return provider;
    }

    private PostgreSqlDeadlockInjector _Injector()
    {
        return new PostgreSqlDeadlockInjector(fixture.ConnectionString, _Schema, "records");
    }
}
