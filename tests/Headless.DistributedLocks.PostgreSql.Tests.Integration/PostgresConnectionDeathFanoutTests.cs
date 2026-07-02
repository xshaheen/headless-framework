// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

/// <summary>
/// Exercises connection-death detection for the optimistic-multiplexing engine: when two locks share one physical
/// backend and that backend is terminated, the active <see cref="ConnectionMonitor"/> probe must cancel the
/// connection-lost token of <b>every</b> handle riding that connection — not just the one whose token a consumer
/// happened to read. The single-lock StateChange path is already covered by <c>PostgresConnectionDeathTests</c>; this
/// adds the multiplexed fan-out, which is the capability the optimistic engine newly activates.
/// </summary>
[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresConnectionDeathFanoutTests(PostgresDistributedLockFixture fixture) : TestBase
{
    private const string _KeyPrefix = "death-fanout:";

    [Fact]
    public async Task should_cancel_every_multiplexed_handle_when_their_shared_backend_dies()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();

        var resourceA = Faker.Random.AlphaNumeric(12);
        var resourceB = Faker.Random.AlphaNumeric(12);

        // ReleaseOnDispose is false: terminating the backend frees both advisory locks server-side, so disposing the
        // handles must not attempt explicit releases against the now-dead connection.
        var acquireOptions = new DistributedLockAcquireOptions
        {
            ReleaseOnDispose = false,
            Monitoring = LockMonitoringMode.Monitor,
        };

        await using var first = await locks.AcquireAsync(resourceA, acquireOptions, AbortToken);
        await using var second = await locks.AcquireAsync(resourceB, acquireOptions, AbortToken);

        // Reading both lost tokens now forces the engine to register a monitoring handle per lock, switching the
        // ConnectionMonitor from keepalive to its active probe. Both tokens must start uncancelled.
        first.LostToken.IsCancellationRequested.Should().BeFalse();
        second.LostToken.IsCancellationRequested.Should().BeFalse();

        // The fan-out claim only holds if both locks genuinely ride one physical backend; assert that precondition at
        // the DB level before killing it, otherwise the test could pass by killing two independent connections.
        var pid = await _GetSingleSharedBackendPidAsync(resourceA, resourceB);

        // Terminate exactly that backend from a separate admin connection and assert it was actually killed.
        await _TerminateBackendAsync(pid);

        // Wait for BOTH tokens to fire, bounded by a 15s deadline and linked to AbortToken. The active monitoring
        // probe (a server-side sleep with a bounded command timeout) is what surfaces the death on an otherwise-idle
        // holder — not a fixed sleep here. A genuine timeout falls through to the assertions so the test fails loudly.
        using var both = CancellationTokenSource.CreateLinkedTokenSource(first.LostToken, second.LostToken);

        await _WaitUntilAsync(
            () => first.LostToken.IsCancellationRequested && second.LostToken.IsCancellationRequested,
            timeout: TimeSpan.FromSeconds(15),
            wake: both.Token
        );

        first
            .LostToken.IsCancellationRequested.Should()
            .BeTrue("the first multiplexed handle must observe the backend death");
        second
            .LostToken.IsCancellationRequested.Should()
            .BeTrue("the second multiplexed handle must observe the backend death");
    }

    /// <summary>
    /// Resolves the single backend pid that holds both resources' advisory locks, failing the test if the two locks did
    /// not actually share one physical connection (the multiplexing precondition for the fan-out assertion).
    /// </summary>
    private async Task<int> _GetSingleSharedBackendPidAsync(string resourceA, string resourceB)
    {
        var pidsForA = await _GetBackendPidsHoldingAsync(_KeyPrefix + resourceA);
        var pidsForB = await _GetBackendPidsHoldingAsync(_KeyPrefix + resourceB);

        pidsForA.Should().ContainSingle("resource A must be held by exactly one backend");
        pidsForB.Should().ContainSingle("resource B must be held by exactly one backend");
        pidsForB
            .Should()
            .BeEquivalentTo(pidsForA, "both locks must be multiplexed onto one shared backend before termination");

        return pidsForA[0];
    }

    private async Task<IReadOnlyList<int>> _GetBackendPidsHoldingAsync(string keyMaterial)
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

        var pids = new List<int>();

        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        while (await reader.ReadAsync(AbortToken))
        {
            pids.Add(reader.GetInt32(0));
        }

        return pids;
    }

    private async Task _TerminateBackendAsync(int pid)
    {
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync(AbortToken);

        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT pg_terminate_backend(@pid)";
        command.Parameters.AddWithValue(nameof(pid), pid);

        // Capture the result: a false/null return means the backend was not actually terminated, so the test must fail
        // here rather than later asserting on a connection that was never killed.
        var terminated = await command.ExecuteScalarAsync(AbortToken);
        terminated.Should().Be(true, "the shared lock-holding backend should be found and terminated");
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses. The wait between polls
    /// is woken by <paramref name="wake"/> (the linked lost-tokens) so detection ends the wait immediately rather than
    /// waiting out a fixed interval; AbortToken cancels the whole wait. A timeout simply returns, leaving the caller's
    /// assertions to decide the outcome.
    /// </summary>
    private async Task _WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken wake)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        deadline.CancelAfter(timeout);

        while (!condition())
        {
            using var step = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, wake);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), step.Token);
            }
            catch (OperationCanceledException) when (AbortToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The wake token fired (a handle was lost): loop re-evaluates the condition immediately.
            }
        }
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();

        // A short TCP keepalive lets a terminated backend surface promptly via StateChange, and a short command timeout
        // bounds the monitor's active probe so a silently-dead connection faults quickly instead of hanging on the
        // server-side sleep. Both back the connection-lost token; the test asserts the token, not a specific mechanism.
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { KeepAlive = 1 };

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = builder.ConnectionString;
                options.KeyPrefix = _KeyPrefix;
                options.CommandTimeout = TimeSpan.FromSeconds(2);
                options.KeepAlive = TimeSpan.FromSeconds(1);
            })
        );

        return services.BuildServiceProvider();
    }
}
