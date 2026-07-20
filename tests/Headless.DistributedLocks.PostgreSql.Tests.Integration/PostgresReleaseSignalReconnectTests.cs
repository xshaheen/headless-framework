// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresReleaseSignalReconnectTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    private const string _Channel = "headless_distributed_locks_release";

    [Fact]
    public async Task should_reestablish_listen_and_wake_a_waiter_after_the_listener_backend_is_terminated()
    {
        // A large polling fallback removes polling as a competing wake path within the test budget, so a
        // contender that wakes after the listener backend is killed proves the LISTEN connection was
        // re-established (the NOTIFY is the only realistic wake source in this window).
        var keyPrefix = $"reconnect:{Faker.Random.AlphaNumeric(6)}:";
        await using var provider = _CreateProvider(keyPrefix, pollingFallback: TimeSpan.FromSeconds(30));
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        // Wait until the signal's listener backend is up so we have a baseline to detect the reconnect.
        await _WaitForListenerCountAtLeastAsync(1);
        var listenerPidBeforeKill = await _GetFirstListenerPidAsync();

        // Terminate the listener backend; the signal's background loop should reconnect with backoff.
        await _TerminateListenerBackendsAsync();

        // Confirm a NEW listener backend is established (different pid than the killed one).
        await _WaitForReconnectAsync(listenerPidBeforeKill);

        var first = await locks.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(60) },
            AbortToken
        );

        var contender = Task.Run(
            async () =>
                await locks.AcquireAsync(
                    resource,
                    new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(60) },
                    AbortToken
                ),
            AbortToken
        );

        await _WaitForHeldLockAsync(keyPrefix + resource);
        contender.IsCompleted.Should().BeFalse();

        await first.ReleaseAsync();

        // The reconnected listener must deliver the release NOTIFY and wake the contender well within the
        // budget — far faster than the 30s polling fallback, proving the push path is live again.
        var second = await contender.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        await using var _ = second;
        second.Should().NotBeNull();
        second.Resource.Should().Be(resource);
    }

    private async Task _WaitForListenerCountAtLeastAsync(int minimum)
    {
        using var timeout = TimeProvider.System.CreateCancellationTokenSource(TimeSpan.FromSeconds(15));

        while (await _CountListenerBackendsAsync() < minimum)
        {
            timeout.Token.IsCancellationRequested.Should().BeFalse("the release-signal listener backend should appear");
            await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        }
    }

    private async Task _WaitForReconnectAsync(int? oldPid)
    {
        using var timeout = TimeProvider.System.CreateCancellationTokenSource(TimeSpan.FromSeconds(30));

        while (true)
        {
            var current = await _GetFirstListenerPidAsync();

            if (current is not null && current != oldPid)
            {
                return;
            }

            timeout.Token.IsCancellationRequested.Should().BeFalse("the listener should reconnect on a fresh backend");
            await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }
    }

    private async Task<int> _CountListenerBackendsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM pg_catalog.pg_stat_activity
            WHERE query = @query
              AND datname = current_database()
              AND pid <> pg_backend_pid()
            """;
        command.Parameters.AddWithValue("query", $"LISTEN {_Channel}");

        return (int)(long)(await command.ExecuteScalarAsync(AbortToken) ?? 0L);
    }

    private async Task<int?> _GetFirstListenerPidAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pid
            FROM pg_catalog.pg_stat_activity
            WHERE query = @query
              AND datname = current_database()
              AND pid <> pg_backend_pid()
            ORDER BY pid
            LIMIT 1
            """;
        command.Parameters.AddWithValue("query", $"LISTEN {_Channel}");

        return (int?)await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task _TerminateListenerBackendsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pg_terminate_backend(pid)
            FROM pg_catalog.pg_stat_activity
            WHERE query = @query
              AND datname = current_database()
              AND pid <> pg_backend_pid()
            """;
        command.Parameters.AddWithValue("query", $"LISTEN {_Channel}");
        await command.ExecuteNonQueryAsync(AbortToken);
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

    private ServiceProvider _CreateProvider(string keyPrefix, TimeSpan pollingFallback)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = keyPrefix;
                options.EnablePushWakeup = true;
                options.PollingFallback = pollingFallback;
            })
        );

        return services.BuildServiceProvider();
    }
}
