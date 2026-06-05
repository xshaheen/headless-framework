// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.ConnectionScopedLocks;

public sealed class ConnectionScopedDistributedLockProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeConnectionScopedLockStorage _storage = new();
    private readonly FakeReleaseSignal _releaseSignal = new();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();

    private long _lockIdCounter = 1000;

    [Fact]
    public async Task should_release_acquired_storage_handle_when_fencing_token_source_fails()
    {
        var provider = _CreateProvider(fencingTokenSource: new ThrowingFencingTokenSource());
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () => await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _storage.ReleaseCount.Should().Be(1);
        _releaseSignal.PublishCount.Should().Be(1);
    }

    [Fact]
    public async Task should_jitter_polling_fallback_across_waits_when_waiting_for_contention()
    {
        // 50 contended attempts before success forces 50 jittered waits on the same resource.
        const int contendedAttempts = 50;
        var pollingFallback = TimeSpan.FromSeconds(7);

        for (var i = 0; i < contendedAttempts; i++)
        {
            _storage.AcquireResults.Enqueue(false);
        }

        _storage.AcquireResults.Enqueue(true);

        var provider = _CreateProvider(pollingFallback: pollingFallback);
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(10) },
            AbortToken
        );

        handle.Should().NotBeNull();

        var waits = _releaseSignal.WaitDurations;
        waits.Should().HaveCount(contendedAttempts);

        // Every wait must stay inside the [0.8, 1.2) jitter band around the base fallback.
        waits
            .Should()
            .AllSatisfy(wait =>
                wait.Should()
                    .BeGreaterThanOrEqualTo(pollingFallback * 0.8)
                    .And.BeLessThanOrEqualTo(pollingFallback * 1.2)
            );

        // Prove jitter is actually applied: a no-jitter implementation would return exactly the base
        // every time. Require distinct values that fall on BOTH sides of the base fallback so the test
        // cannot pass against an impl that just returns a constant (even a constant != base).
        waits.Distinct().Should().HaveCountGreaterThan(1);
        waits.Should().Contain(wait => wait < pollingFallback);
        waits.Should().Contain(wait => wait > pollingFallback);
    }

    [Fact]
    public async Task should_emit_a_lock_acquire_activity_with_the_resource_tag_when_a_lock_is_acquired()
    {
        // given (listen to the distributed-locks activity source)
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Headless.DistributedLocks",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        // when (an uncontended acquire — the storage fake grants immediately)
        await using var handle = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // then (the connection-scoped provider now emits the same lock.acquire activity as the regular provider).
        // Filter by this test's unique resource tag so activities emitted by test classes running in parallel — the
        // listener is process-wide — cannot pollute the assertion.
        activities
            .Should()
            .ContainSingle(a =>
                a.OperationName == "lock.acquire" && (string?)a.GetTagItem("headless.lock.resource") == resource
            );
    }

    [Fact]
    public async Task should_throw_when_max_waiters_per_resource_is_exceeded()
    {
        // One waiter slot per resource: the second concurrent acquirer on the same resource must be rejected.
        var options = new DistributedLockOptions { MaxWaitersPerResource = 1 };
        var blockingSignal = new BlockingReleaseSignal();
        var alwaysContended = new AlwaysContendedStorage();

        var provider = _CreateProvider(options: options, storage: alwaysContended, releaseSignal: blockingSignal);
        var resource = Faker.Random.AlphaNumeric(12);

        var acquireOptions = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(10) };

        // First acquirer blocks as the single allowed waiter.
        var first = provider.TryAcquireAsync(resource, acquireOptions, AbortToken);
        await _PollUntilAsync(() => blockingSignal.ActiveWaiters >= 1);

        // Second acquirer on the same resource overflows the per-resource cap.
        var act = async () => await provider.TryAcquireAsync(resource, acquireOptions, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("Maximum waiters per resource");

        blockingSignal.ReleaseAll();
        alwaysContended.GrantNext();

        // Drain the first acquirer so its background loop doesn't outlive the test.
        await using var handle = await first;
    }

    [Fact]
    public async Task should_throw_when_max_concurrent_waiting_resources_is_exceeded()
    {
        // One waiting-resource slot: a second distinct contended resource must be rejected.
        var options = new DistributedLockOptions { MaxConcurrentWaitingResources = 1 };
        var blockingSignal = new BlockingReleaseSignal();
        var alwaysContended = new AlwaysContendedStorage();

        var provider = _CreateProvider(options: options, storage: alwaysContended, releaseSignal: blockingSignal);

        var acquireOptions = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(10) };

        var first = provider.TryAcquireAsync(Faker.Random.AlphaNumeric(12), acquireOptions, AbortToken);
        await _PollUntilAsync(() => blockingSignal.ActiveWaiters >= 1);

        var act = async () => await provider.TryAcquireAsync(Faker.Random.AlphaNumeric(12), acquireOptions, AbortToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("Maximum concurrent waiting resources");

        blockingSignal.ReleaseAll();
        alwaysContended.GrantNext();

        await using var handle = await first;
    }

    [Fact]
    public async Task should_throw_lock_acquisition_timeout_when_acquire_times_out()
    {
        // Storage never grants; FakeReleaseSignal returns immediately, so the loop spins until the
        // (real-clock) zero-budget deadline forces the timeout. A zero acquire timeout is a single
        // try-once attempt that throws on contention.
        for (var i = 0; i < 100; i++)
        {
            _storage.AcquireResults.Enqueue(false);
        }

        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () =>
            await provider.AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

        await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
    }

    [Fact]
    public async Task should_throw_lock_acquisition_timeout_on_read_lock_acquire_timeout()
    {
        for (var i = 0; i < 100; i++)
        {
            _storage.AcquireResults.Enqueue(false);
        }

        var rwProvider = new ConnectionScopedReaderWriterLockProvider(_CreateProvider());
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () =>
            await rwProvider.AcquireReadLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

        await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
    }

    [Fact]
    public async Task should_throw_lock_acquisition_timeout_on_write_lock_acquire_timeout()
    {
        for (var i = 0; i < 100; i++)
        {
            _storage.AcquireResults.Enqueue(false);
        }

        var rwProvider = new ConnectionScopedReaderWriterLockProvider(_CreateProvider());
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () =>
            await rwProvider.AcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

        await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
    }

    private async Task _PollUntilAsync(Func<bool> condition)
    {
        while (!condition())
        {
            AbortToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    [Fact]
    public async Task should_call_server_blocking_storage_once_with_full_timeout_and_skip_release_signal()
    {
        var acquireTimeout = TimeSpan.FromSeconds(13);
        _storage.BlocksServerSide = true;
        _storage.AcquireResults.Enqueue(false);

        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = acquireTimeout },
            AbortToken
        );

        handle.Should().BeNull();
        _storage.AcquireCount.Should().Be(1);
        _storage.AcquireTimeouts.Should().ContainSingle().Which.Should().Be(acquireTimeout);
        _releaseSignal.WaitDurations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_enforce_waiter_guardrails_before_server_blocking_storage_call()
    {
        _storage.BlocksServerSide = true;
        _storage.BlockAcquire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage.AcquireResults.Enqueue(true);

        var provider = _CreateProvider(options: new DistributedLockOptions { MaxConcurrentWaitingResources = 1 });

#pragma warning disable AsyncFixer04 // Intentionally not awaited so the first acquire remains blocked in storage.
        var first = provider.TryAcquireAsync(
            "resource-1",
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );
#pragma warning restore AsyncFixer04

        await _storage.WaitForAcquireAsync(AbortToken);

        var act = async () =>
            await provider.TryAcquireAsync(
                "resource-2",
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Maximum concurrent waiting resources*");

        _storage.BlockAcquire.SetResult();
        await using var handle = await first;
        handle.Should().NotBeNull();
    }

    private ConnectionScopedDistributedLockProvider _CreateProvider(
        IFencingTokenSource? fencingTokenSource = null,
        TimeSpan? pollingFallback = null,
        DistributedLockOptions? options = null,
        IConnectionScopedLockStorage? storage = null,
        IReleaseSignal? releaseSignal = null
    )
    {
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new ConnectionScopedDistributedLockProvider(
            storage ?? _storage,
            releaseSignal ?? _releaseSignal,
            options ?? new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<ConnectionScopedDistributedLockProvider>(),
            fencingTokenSource,
            pollingFallback
        );
    }

    private sealed class FakeConnectionScopedLockStorage : IConnectionScopedLockStorage
    {
        public Queue<bool> AcquireResults { get; } = new();

        public List<TimeSpan> AcquireTimeouts { get; } = [];

        public TaskCompletionSource? BlockAcquire { get; set; }

        public int AcquireCount { get; private set; }

        public bool BlocksServerSide { get; set; }

        public int ReleaseCount { get; private set; }

        public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
            string resource,
            string lockId,
            bool isShared,
            TimeSpan acquireTimeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCount++;
            AcquireTimeouts.Add(acquireTimeout);

            if (BlockAcquire is not null)
            {
                await BlockAcquire.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var acquired = AcquireResults.Count == 0 || AcquireResults.Dequeue();

            return acquired ? new ConnectionScopedLockHandle(resource, lockId, ReleaseAsync, CancellationToken.None) : null;
        }

        public async Task WaitForAcquireAsync(CancellationToken cancellationToken)
        {
            while (AcquireCount == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsLockedAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(false);
        }

        public ValueTask<long> GetLocksCountAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0L);
        }

        public ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<string?>(null);
        }

        public ValueTask<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<IReadOnlyList<LockInfo>>([]);
        }

        public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0L);
        }
    }

    private sealed class FakeReleaseSignal : IReleaseSignal
    {
        public List<TimeSpan> WaitDurations { get; } = [];

        public int PublishCount { get; private set; }

        public ValueTask WaitAsync(
            string resource,
            TimeSpan pollingFallback,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitDurations.Add(pollingFallback);

            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PublishCount++;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingFencingTokenSource : IFencingTokenSource
    {
        public ValueTask<long?> NextAsync(
            string resource,
            System.Data.Common.DbConnection? connection = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException("fencing failed");
        }
    }

    /// <summary>Storage that reports contention (no grant) until <see cref="GrantNext"/> flips it.</summary>
    private sealed class AlwaysContendedStorage : IConnectionScopedLockStorage
    {
        private volatile bool _grant;

        public void GrantNext() => _grant = true;

        public ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
            string resource,
            string lockId,
            bool isShared,
            TimeSpan acquireTimeout,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult<ConnectionScopedLockHandle?>(
                _grant ? new ConnectionScopedLockHandle(resource, lockId, ReleaseAsync, CancellationToken.None) : null
            );
        }

        public ValueTask ReleaseAsync(
            ConnectionScopedLockHandle handle,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> IsLockedAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(false);

        public ValueTask<long> GetLocksCountAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(0L);

        public ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<LockInfo>>([]);

        public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0L);
    }

    /// <summary>Release signal whose <see cref="WaitAsync"/> blocks until <see cref="ReleaseAll"/> is called.</summary>
    private sealed class BlockingReleaseSignal : IReleaseSignal
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeWaiters;

        public int ActiveWaiters => Volatile.Read(ref _activeWaiters);

        public void ReleaseAll() => _gate.TrySetResult();

        public async ValueTask WaitAsync(
            string resource,
            TimeSpan pollingFallback,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _activeWaiters);

            try
            {
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWaiters);
            }
        }

        public ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
