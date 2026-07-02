// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresConnectionDeathTests(PostgresDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_cancel_handle_lost_token_when_backend_connection_dies()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        // ReleaseOnDispose is false: terminating the backend frees the advisory lock server-side, so
        // disposing the handle must not attempt an explicit release against the now-dead connection.
        await using var handle = await locks.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { ReleaseOnDispose = false, Monitoring = LockMonitoringMode.Monitor },
            AbortToken
        );

        handle.LostToken.IsCancellationRequested.Should().BeFalse();

        // Find and terminate the backend that holds the advisory lock from a separate connection.
        await _TerminateLockHoldingBackendAsync(resource);

        // The StateChange wiring should observe the connection leaving the Open state and cancel the
        // handle-lost token so callers stop trusting the lock. Link the death-detection wait to both
        // AbortToken and the handle-lost token: a test abort cancels the wait (surfaced as a thrown
        // cancellation rather than a wall-clock poll slipping past to assert on stale state), the
        // handle-lost cancellation ends the wait immediately, and a genuine 10s timeout falls through
        // to the assertion so it fails loudly.
        using var detection = CancellationTokenSource.CreateLinkedTokenSource(AbortToken, handle.LostToken);
        detection.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, detection.Token);
        }
        catch (OperationCanceledException) when (AbortToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Either the handle-lost token fired (success) or the 10s timeout elapsed (failure);
            // both fall through to the assertion below, which decides the outcome.
        }

        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    private const string _KeyPrefix = "death:";

    private async Task _TerminateLockHoldingBackendAsync(string resource)
    {
        // Resolve the exact advisory key the provider used so only the lock-holding backend is
        // terminated (avoids collateral termination of other connections in the shared container).
        var key = PostgresAdvisoryLockKey.FromString(_KeyPrefix + resource, allowHashing: true);
        var (key1, key2) = key.Keys;

        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync(AbortToken);

        await using var command = admin.CreateCommand();
        command.CommandText = """
            SELECT pg_terminate_backend(l.pid)
            FROM pg_catalog.pg_locks l
            WHERE l.locktype = 'advisory'
              AND l.granted
              AND l.classid = @classId
              AND l.objid = @objId
              AND l.objsubid = @objSubId
              AND l.pid <> pg_backend_pid()
            """;
        command.Parameters.AddWithValue("classId", key1);
        command.Parameters.AddWithValue("objId", key2);
        command.Parameters.AddWithValue("objSubId", (short)(key.HasSingleKey ? 1 : 2));

        // Capture the result: if no backend matched (null/no rows) or termination failed (false), the
        // test must fail here rather than later asserting on a connection that was never killed.
        var terminated = await command.ExecuteScalarAsync(AbortToken);
        terminated.Should().Be(true, "the lock-holding backend should be found and terminated");
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();

        // Enable Npgsql keepalive so a terminated backend is detected proactively (the held
        // connection is otherwise idle, and StateChange only fires when Npgsql next touches it).
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { KeepAlive = 1 };

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = builder.ConnectionString;
                options.KeyPrefix = _KeyPrefix;
            })
        );

        return services.BuildServiceProvider();
    }
}
