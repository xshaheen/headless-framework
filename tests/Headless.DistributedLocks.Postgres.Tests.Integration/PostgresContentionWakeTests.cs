// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Postgres;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresContentionWakeTests(PostgresDistributedLockFixture fixture) : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_wake_waiting_acquirer_after_holder_releases(bool enablePushWakeup)
    {
        await using var provider = _CreateProvider(enablePushWakeup);
        var locks = provider.GetRequiredService<IDistributedLockProvider>();
        var resource = Faker.Random.AlphaNumeric(12);

        var first = await locks.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        // Second acquirer must block while the first holds the lock.
        var contender = Task.Run(
            async () =>
                await locks.AcquireAsync(
                    resource,
                    new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                    AbortToken
                ),
            AbortToken
        );

        // Give the contender time to enter its wait loop, then confirm it has not acquired yet.
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
        contender.IsCompleted.Should().BeFalse();

        var stopwatch = Stopwatch.StartNew();
        await first.ReleaseAsync();

        var second = await contender.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        stopwatch.Stop();

        await using var _ = second;
        second.Should().NotBeNull();
        second.Resource.Should().Be(resource);

        // The wake-up (push NOTIFY or polling fallback) should free the waiter well within the
        // acquire budget. Polling fallback defaults to 100ms, so a couple of seconds is generous.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private ServiceProvider _CreateProvider(bool enablePushWakeup)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddPostgresDistributedLocks(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.KeyPrefix = $"contention:{Faker.Random.AlphaNumeric(6)}:";
            options.EnablePushWakeup = enablePushWakeup;
        });

        return services.BuildServiceProvider();
    }
}
