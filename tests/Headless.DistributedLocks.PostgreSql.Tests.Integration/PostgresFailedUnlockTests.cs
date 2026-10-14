// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

/// <summary>
/// An advisory unlock that throws leaves the lock held on its session. Npgsql defers its reset until a pooled connection
/// is reused, so returning that session to the pool would keep the lock held for as long as the connection sits idle.
/// These tests observe the lock in <c>pg_locks</c> after an injected unlock failure.
/// </summary>
[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresFailedUnlockTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    private static readonly TimeSpan _FreeWithin = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task should_free_the_advisory_lock_when_its_unlock_throws()
    {
        // given
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var pool = new MultiplexedConnectionLockPool(_ => new PostgresDatabaseConnection(
            dataSource,
            TimeProvider.System,
            monitoringCommandTimeoutSeconds: 5
        ));
        var strategy = new ThrowingReleaseStrategy(new PostgresAdvisoryLock(isShared: false, TimeProvider.System));
        var resource = "postgres-failed-unlock:" + Faker.Random.Guid();

        var handle = await pool.TryAcquireAsync(
            dataSource.ConnectionString,
            resource,
            TimeSpan.Zero,
            strategy,
            Timeout.InfiniteTimeSpan,
            AbortToken
        );
        handle.Should().NotBeNull();
        (await _IsHeldAsync(resource)).Should().BeTrue();

        // when
        var release = async () => await handle!.DisposeAsync();

        // then
        await release.Should().ThrowAsync<InvalidOperationException>();
        (await _WaitUntilFreeAsync(resource)).Should().BeTrue("the session holding the failed lock must be ended");
    }

    private async Task<bool> _WaitUntilFreeAsync(string resource)
    {
        var started = TimeProvider.System.GetTimestamp();

        while (TimeProvider.System.GetElapsedTime(started) < _FreeWithin)
        {
            if (!await _IsHeldAsync(resource))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), AbortToken);
        }

        return !await _IsHeldAsync(resource);
    }

    private async Task<bool> _IsHeldAsync(string resource)
    {
        var key = PostgreSqlAdvisoryLockKey.FromString(resource, allowHashing: true);
        var (key1, key2) = key.Keys;

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

        return (long)(await command.ExecuteScalarAsync(AbortToken))! > 0;
    }

    /// <summary>Delegates to the real advisory strategy but fails every release before it reaches the server.</summary>
    private sealed class ThrowingReleaseStrategy(PostgresAdvisoryLock inner) : IDbSynchronizationStrategy<object>
    {
        public bool IsUpgradeable => inner.IsUpgradeable;

        public object GetHeldLockIdentity(string resourceName)
        {
            return inner.GetHeldLockIdentity(resourceName);
        }

        public ValueTask<object?> TryAcquireAsync(
            DatabaseConnection connection,
            string resourceName,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            return inner.TryAcquireAsync(connection, resourceName, timeout, cancellationToken);
        }

        public ValueTask ReleaseAsync(DatabaseConnection connection, string resourceName, object lockCookie)
        {
            throw new InvalidOperationException("Simulated unlock failure.");
        }
    }
}
