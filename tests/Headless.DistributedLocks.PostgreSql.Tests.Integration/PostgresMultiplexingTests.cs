// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

/// <summary>
/// Observes the optimistic-multiplexing engine at the database level: distinct advisory keys acquired from one provider
/// share a single physical backend connection, while a colliding key (the same shared advisory key, forced off the
/// shared connection by guard #1) lands on a separate backend. Backend identity is read from <c>pg_locks.pid</c>, which
/// is the PostgreSQL backend process id of the session holding each granted advisory lock — two locks on the same pid
/// prove they ride the same physical connection.
/// </summary>
[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresMultiplexingTests(PostgresDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_share_one_backend_connection_when_two_distinct_resources_are_locked()
    {
        var keyPrefix = $"mux-share:{Faker.Random.AlphaNumeric(8)}:";
        await using var provider = _CreateProvider(keyPrefix);
        var locks = provider.GetRequiredService<IDistributedLock>();

        var resourceA = Faker.Random.AlphaNumeric(12);
        var resourceB = Faker.Random.AlphaNumeric(12);

        // Acquire sequentially so the engine's opportunistic phase can reuse the connection the first lock left in the
        // pool. The two resources resolve to two distinct advisory keys, so neither collides with the other (guard #1
        // does not fire) and both can be held on one physical connection.
        await using var first = await locks.AcquireAsync(resourceA, cancellationToken: AbortToken);
        await using var second = await locks.AcquireAsync(resourceB, cancellationToken: AbortToken);

        var pidsForA = await _GetBackendPidsHoldingAsync(keyPrefix + resourceA);
        var pidsForB = await _GetBackendPidsHoldingAsync(keyPrefix + resourceB);

        // Each key must be held by exactly one backend (a single in-process holder), and both keys' lone backends must
        // be the same pid — the engine multiplexed both advisory locks onto one physical connection. Distinct keys
        // rules out the assertion being satisfied incidentally by the two resources colliding to one lock.
        pidsForA.Should().ContainSingle("resource A is held by exactly one backend");
        pidsForB.Should().ContainSingle("resource B is held by exactly one backend");
        pidsForB.Should().BeEquivalentTo(pidsForA, "both advisory locks must share a single multiplexed backend");
    }

    [Fact]
    public async Task should_dedicate_a_separate_backend_when_a_colliding_key_is_acquired()
    {
        var keyPrefix = $"mux-collide:{Faker.Random.AlphaNumeric(8)}:";
        await using var provider = _CreateProvider(keyPrefix);
        var locks = provider.GetRequiredService<IDistributedReadWriteLock>();

        // Two read (shared) locks on the SAME resource resolve to one advisory key. The first lands on a multiplexed
        // connection; the second collides on that connection's held-identity set (guard #1) and is forced onto a
        // dedicated connection. Shared advisory locks are mutually compatible, so the dedicated acquire still succeeds —
        // unlike an exclusive collision, which could never be granted on a second backend and so could not prove
        // dedication. The two readers therefore end up on two distinct backends holding the same shared key.
        var resource = Faker.Random.AlphaNumeric(12);

        await using var firstReader = await locks.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        await using var secondReader = await locks.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        var pids = await _GetBackendPidsHoldingAsync(keyPrefix + resource, isShared: true);

        // Both readers are confirmed held server-side (count == 2), and they sit on two distinct backend pids — the
        // colliding second acquirer was dedicated to its own physical connection rather than sharing the first's.
        (await locks.GetReaderCountAsync(resource, AbortToken))
            .Should()
            .Be(2);
        pids.Should().HaveCount(2, "the colliding shared lock must be dedicated to a separate backend connection");
    }

    [Fact]
    public async Task should_release_outstanding_advisory_locks_when_the_provider_is_disposed_without_disposing_handles()
    {
        var keyPrefix = $"mux-dispose:{Faker.Random.AlphaNumeric(8)}:";
        var resourceA = Faker.Random.AlphaNumeric(12);
        var resourceB = Faker.Random.AlphaNumeric(12);

        var provider = _CreateProvider(keyPrefix);
        var locks = provider.GetRequiredService<IDistributedLock>();

        // Acquire two locks and DELIBERATELY never dispose the handles. Connection-scoped advisory locks have no TTL and
        // no GC finalizer reclaim, so disposing the provider is what must release them — this pins the documented
        // lifecycle contract (storage disposal tears down every outstanding held lock, then the data source).
        _ = await locks.AcquireAsync(resourceA, cancellationToken: AbortToken);
        _ = await locks.AcquireAsync(resourceB, cancellationToken: AbortToken);

        // sanity: both are genuinely held server-side before disposal (otherwise the post-dispose assertion is vacuous)
        (await _GetBackendPidsHoldingAsync(keyPrefix + resourceA))
            .Should()
            .NotBeEmpty("resource A is held before dispose");
        (await _GetBackendPidsHoldingAsync(keyPrefix + resourceB))
            .Should()
            .NotBeEmpty("resource B is held before dispose");

        // when (the provider is disposed while both handles are still outstanding)
        await provider.DisposeAsync();

        // then (provider disposal releases every outstanding advisory lock — poll bound to the test token, no fixed sleep)
        await _WaitUntilAsync(async () =>
            (await _GetBackendPidsHoldingAsync(keyPrefix + resourceA)).Count == 0
            && (await _GetBackendPidsHoldingAsync(keyPrefix + resourceB)).Count == 0
        );

        (await _GetBackendPidsHoldingAsync(keyPrefix + resourceA))
            .Should()
            .BeEmpty("provider disposal must release resource A");
        (await _GetBackendPidsHoldingAsync(keyPrefix + resourceB))
            .Should()
            .BeEmpty("provider disposal must release resource B");
    }

    /// <summary>Polls <paramref name="condition"/> until true or a bounded deadline, linked to the test abort token.</summary>
    private async Task _WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var deadlineCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(AbortToken, deadlineCts.Token);

        while (!await condition())
        {
            // A test-runner abort propagates; a deadline becomes a clear assertion failure (not an opaque OCE).
            AbortToken.ThrowIfCancellationRequested();

            deadlineCts
                .IsCancellationRequested.Should()
                .BeFalse("the condition was not satisfied within the 15s deadline");

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), linked.Token);
            }
            catch (OperationCanceledException)
                when (deadlineCts.IsCancellationRequested && !AbortToken.IsCancellationRequested)
            {
                deadlineCts
                    .IsCancellationRequested.Should()
                    .BeFalse("the condition was not satisfied within the 15s deadline");
            }
        }
    }

    /// <summary>
    /// Returns the distinct set of backend process ids (<c>pg_locks.pid</c>) currently holding a granted advisory lock
    /// for <paramref name="keyMaterial"/> in the current database. Filtering on the exact (classid, objid, objsubid)
    /// triple and on <c>granted</c> keeps the result scoped to this test's resource even on a shared container.
    /// </summary>
    private async Task<IReadOnlyList<int>> _GetBackendPidsHoldingAsync(string keyMaterial, bool? isShared = null)
    {
        var key = PostgresAdvisoryLockKey.FromString(keyMaterial, allowHashing: true);
        var (key1, key2) = key.Keys;

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT l.pid
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

        if (isShared.HasValue)
        {
            command.CommandText += " AND l.mode = @mode";
            command.Parameters.AddWithValue("mode", isShared.Value ? "ShareLock" : "ExclusiveLock");
        }

        var pids = new List<int>();

        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        while (await reader.ReadAsync(AbortToken))
        {
            pids.Add(reader.GetInt32(0));
        }

        return pids;
    }

    private ServiceProvider _CreateProvider(string keyPrefix)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = keyPrefix;
            })
        );

        return services.BuildServiceProvider();
    }
}
