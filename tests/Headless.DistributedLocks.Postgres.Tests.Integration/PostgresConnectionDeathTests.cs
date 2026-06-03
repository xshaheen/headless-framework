// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.Postgres;
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
        var locks = provider.GetRequiredService<IDistributedLockProvider>();
        var resource = Faker.Random.AlphaNumeric(12);

        // ReleaseOnDispose is false: terminating the backend frees the advisory lock server-side, so
        // disposing the handle must not attempt an explicit release against the now-dead connection.
        await using var handle = await locks.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        // Find and terminate the backend that holds the advisory lock from a separate connection.
        await _TerminateLockHoldingBackendAsync(resource);

        // The StateChange wiring should observe the connection leaving the Open state and cancel the
        // handle-lost token so callers stop trusting the lock.
        using var timeout = TimeProvider.System.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!handle.HandleLostToken.IsCancellationRequested && !timeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    private const string _KeyPrefix = "death:";

    private async Task _TerminateLockHoldingBackendAsync(string resource)
    {
        // Resolve the exact advisory key the provider used so only the lock-holding backend is
        // terminated (avoids collateral termination of other connections in the shared container).
        var key = PostgresAdvisoryLockKey.FromString(_KeyPrefix + resource, allowHashing: true);
        var keys = key.Keys;

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
        command.Parameters.AddWithValue("classId", keys.Key1);
        command.Parameters.AddWithValue("objId", keys.Key2);
        command.Parameters.AddWithValue("objSubId", (short)(key.HasSingleKey ? 1 : 2));
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();

        // Enable Npgsql keepalive so a terminated backend is detected proactively (the held
        // connection is otherwise idle, and StateChange only fires when Npgsql next touches it).
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { KeepAlive = 1 };

        services.AddLogging();
        services.AddPostgresDistributedLocks(options =>
        {
            options.ConnectionString = builder.ConnectionString;
            options.KeyPrefix = _KeyPrefix;
        });

        return services.BuildServiceProvider();
    }
}
