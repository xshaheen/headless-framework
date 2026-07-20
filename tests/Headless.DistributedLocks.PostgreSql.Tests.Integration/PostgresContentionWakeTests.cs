// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresContentionWakeTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_wake_waiting_acquirer_when_holder_releases(bool enablePushWakeup)
    {
        var keyPrefix = $"contention:{Faker.Random.AlphaNumeric(6)}:";
        await using var provider = _CreateProvider(enablePushWakeup, keyPrefix);
        var locks = provider.GetRequiredService<IDistributedLock>();
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

        // Prove the lock is genuinely held before the negative assertion instead of sleeping a fixed
        // interval: the provider acquires via non-blocking pg_try_advisory_lock + application polling,
        // so the contender never produces an ungranted pg_locks row. Wait until Postgres reports the
        // holder's granted advisory lock for this resource — that lock is exactly what forces every
        // contender pg_try_advisory_lock to fail — then assert the contender has not acquired.
        await _WaitForHeldLockAsync(keyPrefix + resource);
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

    private async Task _WaitForHeldLockAsync(string keyMaterial)
    {
        var key = PostgreSqlAdvisoryLockKey.FromString(keyMaterial, allowHashing: true);
        var (key1, key2) = key.Keys;

        using var timeout = TimeProvider.System.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM pg_catalog.pg_locks l
                JOIN pg_catalog.pg_database d ON d.oid = l.database
                WHERE l.locktype = 'advisory'
                  AND l.granted
                  AND d.datname = pg_catalog.current_database()
                  AND l.classid = @classId
                  AND l.objid = @objId
                  AND l.objsubid = @objSubId
                """;
            command.Parameters.AddWithValue("classId", key1);
            command.Parameters.AddWithValue("objId", key2);
            command.Parameters.AddWithValue("objSubId", (short)(key.HasSingleKey ? 1 : 2));

            var held = (long)(await command.ExecuteScalarAsync(AbortToken) ?? 0L);

            if (held > 0)
            {
                return;
            }

            timeout.Token.IsCancellationRequested.Should().BeFalse("the holder's advisory lock should be granted");
            await Task.Delay(TimeSpan.FromMilliseconds(25), AbortToken);
        }
    }

    private ServiceProvider _CreateProvider(bool enablePushWakeup, string keyPrefix)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = keyPrefix;
                options.EnablePushWakeup = enablePushWakeup;
            })
        );

        return services.BuildServiceProvider();
    }
}
