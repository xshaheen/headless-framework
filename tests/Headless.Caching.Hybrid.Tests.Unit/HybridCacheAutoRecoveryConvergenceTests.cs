// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Two-instance auto-recovery convergence suite (FusionCache
/// CanHandleIssuesWithBothDistributedCacheAndBackplaneAsync /
/// CanHandleReconnectedBackplaneWithoutReconnectedDistributedCacheAsync analogs). Two
/// <see cref="HybridCache"/> nodes share one L2 backend (each behind its own faultable facade so outages can be
/// per-instance) and one synchronous in-memory backplane bus, all driven by a single
/// <see cref="FakeTimeProvider"/> so every replay pass is deterministic.
/// </summary>
public sealed class HybridCacheAutoRecoveryConvergenceTests : TestBase
{
    private static readonly TimeSpan _Delay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _Ttl = TimeSpan.FromMinutes(30);

    [Fact]
    public async Task should_converge_on_last_writer_value_when_full_outage_writes_replay_oldest_writer_first()
    {
        // given — L2 and the backplane are both down for both nodes
        await using var harness = new TwoNodeConvergenceHarness();
        var (a, b) = (harness.A, harness.B);
        harness.Bus.State = FakeBackplaneState.Down;
        a.L2.FailWrites = true;
        b.L2.FailWrites = true;

        var key = Faker.Random.AlphaNumeric(10);

        // when — A writes v-a, then B writes v-b one second later (distinct conflict timestamps)
        await a.Cache.UpsertAsync(key, "v-a", _Ttl, AbortToken);
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await b.Cache.UpsertAsync(key, "v-b", _Ttl, AbortToken);

        // then — each caller succeeded against its own L1 and queued exactly one recovery item, and it is the
        // VALUE write that survives: the failed publish never displaces a queued value op (the value op
        // subsumes it because its replay publishes the key invalidation itself).
        a.Cache.RecoveryQueue!.Count.Should().Be(1);
        b.Cache.RecoveryQueue!.Count.Should().Be(1);
        a.Cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.SetEntry);
        b.Cache.RecoveryQueue.GetKind(key).Should().Be(HybridCacheRecoveryKind.SetEntry);
        (await a.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-a");
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");

        // when — both dependencies recover and the replay loops run, the older writer (A) first
        harness.Bus.State = FakeBackplaneState.Up;
        a.L2.FailWrites = false;
        b.L2.FailWrites = false;
        await a.Cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — A's replayed Set landed in L2 and republished its invalidation stamped with A's ORIGINAL write
        // time, so B (whose queued write is newer) ignores it instead of wiping its L1: B's pending value and
        // its local copy both survive the older writer's replay.
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("v-a");
        b.Cache.RecoveryQueue.Count.Should().Be(1, "A's older invalidation must not conflict-drop B's newer write");
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");

        await b.Cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the LAST WRITER wins by the conflict/timestamp rules: B's replayed Set overwrites L2 and its
        // republished (newer) invalidation clears A's L1, so every tier converges on v-b.
        a.Cache.RecoveryQueue.Count.Should().Be(0);
        b.Cache.RecoveryQueue.Count.Should().Be(0);
        a.L2.UpsertAttempts.Should().Be(2, "the initial failed write plus the successful replay");
        b.L2.UpsertAttempts.Should().Be(2, "the initial failed write plus the successful replay");
        (await a.L1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse("B's republish cleared A's L1");
        (await a.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await b.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
    }

    [Fact]
    public async Task should_converge_on_last_writer_value_when_full_outage_writes_replay_newest_writer_first()
    {
        // given — the same full-outage divergence as the oldest-first test
        await using var harness = new TwoNodeConvergenceHarness();
        var (a, b) = (harness.A, harness.B);
        harness.Bus.State = FakeBackplaneState.Down;
        a.L2.FailWrites = true;
        b.L2.FailWrites = true;

        var key = Faker.Random.AlphaNumeric(10);
        await a.Cache.UpsertAsync(key, "v-a", _Ttl, AbortToken);
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await b.Cache.UpsertAsync(key, "v-b", _Ttl, AbortToken);

        // when — both dependencies recover but the NEWER writer (B) replays first
        harness.Bus.State = FakeBackplaneState.Up;
        a.L2.FailWrites = false;
        b.L2.FailWrites = false;
        await b.Cache.RecoveryQueue!.ProcessAsync(AbortToken);

        // then — B's replayed Set landed in L2 and its republished invalidation (newer timestamp)
        // conflict-drops A's older queued write and clears A's L1: A has nothing left to replay.
        a.Cache.RecoveryQueue!.Count.Should().Be(0, "A's older queued write lost the timestamp conflict check");
        (await a.L1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        await a.Cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — the winner is the same as in the oldest-first ordering: convergence is replay-order
        // independent, and v-a is never resurrected against L2.
        a.L2.UpsertAttempts.Should().Be(1, "the conflict-dropped write must never be replayed");
        (await a.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await b.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
    }

    [Fact]
    public async Task should_stay_diverged_while_l2_down_after_backplane_restore_then_converge_on_last_recovered_writer()
    {
        // given — L2 is down for both nodes and the backplane is PARTITIONED (publishes are accepted but never
        // delivered). Unlike a throwing backplane (where the failed publish is subsumed by the queued value
        // op), a lossy partition queues no publish at all — the SetEntry is the only pending item either way.
        await using var harness = new TwoNodeConvergenceHarness();
        var (a, b) = (harness.A, harness.B);
        harness.Bus.State = FakeBackplaneState.Lossy;
        a.L2.FailWrites = true;
        b.L2.FailWrites = true;

        var key = Faker.Random.AlphaNumeric(10);
        await a.Cache.UpsertAsync(key, "v-a", _Ttl, AbortToken);
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await b.Cache.UpsertAsync(key, "v-b", _Ttl, AbortToken);

        a.Cache.RecoveryQueue!.Count.Should().Be(1);
        b.Cache.RecoveryQueue!.Count.Should().Be(1);
        a.L2.UpsertAttempts.Should().Be(1);
        b.L2.UpsertAttempts.Should().Be(1);

        // when — only the backplane recovers; the replay cadence elapses (the timer drives one pass per node)
        harness.Bus.State = FakeBackplaneState.Up;
        harness.Time.Advance(_Delay);

        // then — the queued SetEntry replays keep failing and stay queued (retry count advanced by exactly one
        // attempt per pass), and a recovered backplane alone does not converge values: nothing replayed yet,
        // so nothing is republished and each node keeps serving its own value.
        a.Cache.RecoveryQueue.Count.Should().Be(1);
        b.Cache.RecoveryQueue.Count.Should().Be(1);
        a.L2.UpsertAttempts.Should().Be(2, "exactly one failed replay attempt ran");
        b.L2.UpsertAttempts.Should().Be(2, "exactly one failed replay attempt ran");
        (await a.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-a");
        (await b.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");

        // and — the failure barrier is honored: another pass before the cadence elapses attempts nothing
        await a.Cache.RecoveryQueue.ProcessAsync(AbortToken);
        await b.Cache.RecoveryQueue.ProcessAsync(AbortToken);
        a.L2.UpsertAttempts.Should().Be(2, "no replay attempt is allowed before the barrier elapses");
        b.L2.UpsertAttempts.Should().Be(2, "no replay attempt is allowed before the barrier elapses");

        // when — A's L2 connection recovers first and a cadence elapses
        a.L2.FailWrites = false;
        harness.Time.Advance(_Delay);

        // then — A's value lands in L2 and its replayed Set republishes the invalidation stamped with A's
        // original write time, so B (whose pending write is newer) ignores it and keeps both its queued item
        // and its L1 copy; B's own replay is still failing
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("v-a");
        a.Cache.RecoveryQueue.Count.Should().Be(0);
        b.Cache.RecoveryQueue.Count.Should().Be(1);
        b.L2.UpsertAttempts.Should().Be(3);
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");

        // when — B's L2 connection recovers last
        b.L2.FailWrites = false;
        harness.Time.Advance(_Delay);

        // then — the last writer to recover its L2 connection wins the shared tier
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("v-b");
        b.Cache.RecoveryQueue.Count.Should().Be(0);

        // and — B's replayed Set republishes its (newer) invalidation, which clears A's stale L1 copy
        // immediately: the loser converges on the winner's value via the next L2 read instead of waiting for
        // its local TTL to expire.
        (await a.L1.GetAsync<string>(key, AbortToken))
            .HasValue.Should()
            .BeFalse();
        (await a.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await b.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
    }

    [Fact]
    public async Task should_drop_queued_write_and_converge_on_peer_value_when_newer_foreign_write_wins_the_key()
    {
        // given — only A's L2 connection is down; B's L2 and the backplane are healthy
        await using var harness = new TwoNodeConvergenceHarness();
        var (a, b) = (harness.A, harness.B);
        a.L2.FailWrites = true;

        var key = Faker.Random.AlphaNumeric(10);
        await a.Cache.UpsertAsync(key, "v-a", _Ttl, AbortToken);
        a.Cache.RecoveryQueue!.Count.Should().Be(1);
        a.L2.UpsertAttempts.Should().Be(1);

        // when — B successfully writes the same key before A's replay; B's invalidation reaches A
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await b.Cache.UpsertAsync(key, "v-b", _Ttl, AbortToken);

        // then — the newer foreign write wins: A's queued item is conflict-dropped and its L1 copy invalidated
        a.Cache.RecoveryQueue.Count.Should().Be(0);
        (await a.L1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        // when — A's L2 recovers and the replay cadence elapses
        a.L2.FailWrites = false;
        harness.Time.Advance(_Delay);
        await a.Cache.RecoveryQueue.ProcessAsync(AbortToken);

        // then — nothing is replayed (v-a is never resurrected) and A converges on B's value via an L2 read
        a.L2.UpsertAttempts.Should().Be(1, "the conflict-dropped write must never be replayed");
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await a.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
    }

    [Fact]
    public async Task should_drop_peer_stale_l1_copy_when_factory_write_publishes_invalidation()
    {
        // given — every dependency healthy; B holds a stale L1-only copy of the key
        await using var harness = new TwoNodeConvergenceHarness();
        var (a, b) = (harness.A, harness.B);

        var key = Faker.Random.AlphaNumeric(10);
        await b.L1.UpsertAsync(key, "stale", TimeSpan.FromMinutes(5), AbortToken);

        // when — A produces the fresh value through the factory path (factory writes publish invalidations)
        var result = await a.Cache.GetOrAddAsync(key, _ => new ValueTask<string?>("fresh"), _Ttl, AbortToken);

        // then — the factory write published a key invalidation that dropped B's stale copy
        result.Value.Should().Be("fresh");
        b.Cache.InvalidateCacheCalls.Should().Be(1);
        (await b.L1.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();

        // and — B's next read serves A's fresh value from the shared L2 and re-fills its L1 with it
        (await b.Cache.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("fresh");
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task should_give_up_loudly_after_max_retries_with_bounded_attempts_and_leave_instances_divergent()
    {
        // given — A's L2 stays down past the retry budget; the backplane is healthy
        await using var harness = new TwoNodeConvergenceHarness(static o => o.AutoRecoveryMaxRetries = 2);
        var (a, b) = (harness.A, harness.B);
        a.L2.FailWrites = true;

        var key = Faker.Random.AlphaNumeric(10);
        await a.Cache.UpsertAsync(key, "v-a", _Ttl, AbortToken);
        a.L2.UpsertAttempts.Should().Be(1);
        a.Cache.RecoveryQueue!.Count.Should().Be(1);

        // when — the first replay pass fails, the item survives (one retry left)
        harness.Time.Advance(_Delay);
        a.L2.UpsertAttempts.Should().Be(2);
        a.Cache.RecoveryQueue.Count.Should().Be(1);

        // and — the second failed pass exhausts the budget
        harness.Time.Advance(_Delay);

        // then — the item is dropped loudly (warning log event) instead of retrying forever
        a.L2.UpsertAttempts.Should().Be(3);
        a.Cache.RecoveryQueue.Count.Should().Be(0);
        a.Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Event.Name == "AutoRecoveryItemDroppedAfterRetries");

        // and — further cadences attempt nothing: the total L2 attempts stay bounded at initial + MaxRetries
        harness.Time.Advance(_Delay);
        harness.Time.Advance(_Delay);
        a.L2.UpsertAttempts.Should().Be(3, "auto-recovery gave up; no further replay attempts are made");

        // and — the divergence is permanent: A serves its local copy while B and L2 never see the value
        (await a.Cache.GetAsync<string>(key, AbortToken))
            .Value.Should()
            .Be("v-a");
        (await b.Cache.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken)).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_stay_divergent_when_auto_recovery_disabled_and_backplane_recovers_after_outage()
    {
        // given — the negative control for the convergence suite (FusionCache CanBeDisabled analog): auto-recovery
        // is OFF on both nodes and the backplane is down. With L2 healthy, each write lands in the shared L2, but
        // the publish that would drop the peer's stale L1 copy is lost.
        await using var harness = new TwoNodeConvergenceHarness(static o => o.EnableAutoRecovery = false);
        var (a, b) = (harness.A, harness.B);
        harness.Bus.State = FakeBackplaneState.Down;

        var key = Faker.Random.AlphaNumeric(10);

        // seed both nodes' L1 with an initial shared value so each holds a copy that a lost publish should evict
        await a.Cache.UpsertAsync(key, "v-seed", _Ttl, AbortToken);
        await b.Cache.GetOrAddAsync(key, _ => new ValueTask<string?>("v-seed"), _Ttl, AbortToken);
        (await a.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-seed");
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-seed");

        // when — A writes v-a then B writes v-b while the backplane is down; both L2 writes succeed (shared tier),
        // but neither invalidation reaches the peer, and with auto-recovery off nothing is queued for replay.
        await a.Cache.UpsertAsync(key, "v-a", _Ttl, AbortToken);
        harness.Time.Advance(TimeSpan.FromSeconds(1));
        await b.Cache.UpsertAsync(key, "v-b", _Ttl, AbortToken);

        // then — no recovery queue exists at all (auto-recovery disabled), and each node serves its own stale L1
        // copy: A still sees v-a (it never received B's invalidation), B still sees v-b.
        a.Cache.RecoveryQueue.Should().BeNull("auto-recovery is disabled, so no replay machinery exists");
        b.Cache.RecoveryQueue.Should().BeNull();
        (await a.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-a");
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");

        // when — the backplane comes back up and time advances well past any replay cadence
        harness.Bus.State = FakeBackplaneState.Up;
        harness.Time.Advance(_Delay);
        harness.Time.Advance(_Delay);

        // then — PINNED: disabled auto-recovery does NOT replay or republish, so the instances stay DIVERGENT.
        // A keeps serving its stale L1 v-a, B keeps serving v-b, and the shared L2 holds only the last writer's
        // value (v-b) — but no convergence is forced onto the divergent L1 copies.
        a.Cache.RecoveryQueue.Should().BeNull();
        b.Cache.RecoveryQueue.Should().BeNull();
        (await a.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-a", "no republish ever evicted A's L1");
        (await b.L1.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await a.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-a", "A serves its divergent L1 copy");
        (await b.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
        (await harness.SharedL2Backend.GetAsync<string>(key, AbortToken)).Value.Should().Be("v-b");
    }
}

/// <summary>One node of the two-instance harness: the cache, its private L1, its L2 facade, and its log capture.</summary>
internal sealed class ConvergenceNode
{
    public required HybridCache Cache { get; init; }

    public required InMemoryCache L1 { get; init; }

    public required SharedFaultableRemoteCache L2 { get; init; }

    public required RecordingHybridCacheLogger Logger { get; init; }
}

/// <summary>
/// Deterministic two-instance harness: nodes A and B with auto-recovery enabled, each with its own L1, both
/// behind per-instance faultable facades over one SHARED L2 backend, one shared synchronous backplane bus, and
/// one shared <see cref="FakeTimeProvider"/> driving both recovery timer loops.
/// </summary>
internal sealed class TwoNodeConvergenceHarness : IAsyncDisposable
{
    public TwoNodeConvergenceHarness(Action<HybridCacheOptions>? configure = null)
    {
        SharedL2Backend = new InMemoryCache(Time, new InMemoryCacheOptions { CloneValues = true });
        A = _CreateNode("node-a", configure);
        B = _CreateNode("node-b", configure);
        Bus.Attach(A.Cache);
        Bus.Attach(B.Cache);
    }

    public FakeTimeProvider Time { get; } = new();

    public InMemoryCache SharedL2Backend { get; }

    public FakeBackplaneBus Bus { get; } = new();

    public ConvergenceNode A { get; }

    public ConvergenceNode B { get; }

    private ConvergenceNode _CreateNode(string instanceId, Action<HybridCacheOptions>? configure)
    {
        var options = new HybridCacheOptions { EnableAutoRecovery = true, InstanceId = instanceId };
        configure?.Invoke(options);

        var l1 = new InMemoryCache(Time, new InMemoryCacheOptions { CloneValues = true });
        var l2 = new SharedFaultableRemoteCache(SharedL2Backend);
        var logger = new RecordingHybridCacheLogger();
        var cache = new HybridCache(l1, l2, Bus, options, logger, Time);

        return new ConvergenceNode
        {
            Cache = cache,
            L1 = l1,
            L2 = l2,
            Logger = logger,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await A.Cache.DisposeAsync();
        await B.Cache.DisposeAsync();
        A.L1.Dispose();
        B.L1.Dispose();
        SharedL2Backend.Dispose();
    }
}

/// <summary>Captures hybrid-cache log events so tests can pin loud give-up behavior.</summary>
internal sealed class RecordingHybridCacheLogger : ILogger<HybridCache>
{
    public List<(LogLevel Level, EventId Event)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        Entries.Add((logLevel, eventId));
    }
}

/// <summary>
/// Per-instance faultable facade over a SHARED in-memory L2 backend: each hybrid instance gets its own facade
/// (so outages can hit one instance's L2 connection without affecting the other) while all writes land in the
/// same store. Entry sets, scalar upserts, and removes throw while <see cref="FailWrites"/> is on; reads always
/// work; write attempts are counted so tests can assert barrier/retry/replay behavior. (Composes the shared
/// backend instead of reusing <see cref="TogglableRemoteCache"/>, which owns a private store.)
/// </summary>
internal sealed class SharedFaultableRemoteCache(InMemoryCache backend) : IRemoteCache, IFactoryCacheStore
{
    public CacheEntryOptions? DefaultEntryOptions => null;

    /// <summary>When true, entry sets, scalar upserts, and removes throw.</summary>
    public bool FailWrites { get; set; }

    public int SetEntryAttempts { get; private set; }

    public int UpsertAttempts { get; private set; }

    public int RemoveAttempts { get; private set; }

    public ValueTask<CacheStoreEntry<T>> TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    )
    {
        return ((IFactoryCacheStore)backend).TryGetEntryAsync<T>(key, cancellationToken, readOptions);
    }

    public ValueTask<CacheStoreEntry<T>[]> TryGetAllEntriesAsync<T>(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken,
        FactoryCacheReadOptions readOptions = default
    )
    {
        return ((IFactoryCacheStore)backend).TryGetAllEntriesAsync<T>(keys, cancellationToken, readOptions);
    }

    public ValueTask<bool> SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
    {
        SetEntryAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : ((IFactoryCacheStore)backend).SetEntryAsync(key, in entry, cancellationToken);
    }

    public ValueTask TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        return ((IFactoryCacheStore)backend).TryRearmSlidingAsync(
            key,
            slidingExpiration,
            physicalExpiresAt,
            now,
            cancellationToken
        );
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return backend.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return backend.GetOrAddAsync(key, factory, options, cancellationToken);
    }

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        UpsertAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : backend.UpsertAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        return backend.UpsertEntryAsync(key, value, options, cancellationToken);
    }

    public ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.UpsertAllAsync(value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.TryInsertAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.TryReplaceAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.TryReplaceIfEqualAsync(key, expected, value, expiration, cancellationToken);
    }

    public ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.IncrementAsync(key, amount, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.SetIfHigherAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.SetIfLowerAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.SetAddAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        return backend.GetAllAsync<T>(cacheKeys, cancellationToken);
    }

    public async ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        var value = await backend.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (!value.HasValue)
        {
            return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
        }

        var expiration = await backend.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
        return new CacheValueWithExpiration<T>(value, expiration);
    }

    public async ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var values = await backend.GetAllAsync<T>(cacheKeys, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, CacheValueWithExpiration<T>>(values.Count, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            var expiration = await backend.GetExpirationAsync(key, cancellationToken).ConfigureAwait(false);
            result[key] = new CacheValueWithExpiration<T>(value, expiration);
        }

        return result;
    }

    public ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return backend.GetByPrefixAsync<T>(prefix, cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return backend.GetAllKeysByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        return backend.GetAsync<T>(key, cancellationToken);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return backend.GetCountAsync(prefix, cancellationToken);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return backend.ExistsAsync(key, cancellationToken);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return backend.GetExpirationAsync(key, cancellationToken);
    }

    public ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        return backend.GetSetAsync<T>(key, pageIndex, pageSize, cancellationToken);
    }

    public ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        return backend.RefreshAsync(key, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : backend.RemoveAsync(key, cancellationToken);
    }

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        RemoveAttempts++;

        return FailWrites
            ? throw new InvalidOperationException("L2 write failed")
            : backend.ExpireAsync(key, cancellationToken);
    }

    public ValueTask<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
    {
        return backend.RemoveIfEqualAsync(key, expected, cancellationToken);
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        return backend.RemoveAllAsync(cacheKeys, cancellationToken);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return backend.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return backend.RemoveByTagAsync(tag, cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        return backend.ClearAsync(cancellationToken);
    }

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        return backend.SetRemoveAsync(key, value, expiration, cancellationToken);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return backend.FlushAsync(cancellationToken);
    }
}
