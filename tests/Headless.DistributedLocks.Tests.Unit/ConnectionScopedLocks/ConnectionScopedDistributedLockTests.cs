// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.ConnectionScopedLocks;

public sealed class ConnectionScopedDistributedLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeConnectionScopedLockStorage _storage = new();
    private readonly FakeReleaseSignal _releaseSignal = new();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    [Fact]
    public void should_expose_injected_time_provider()
    {
        var provider = _CreateProvider();

        provider.TimeProvider.Should().BeSameAs(_timeProvider);
    }

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
    public async Task should_issue_guid_formatted_lease_id()
    {
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        _guidGenerator.Create().Returns(guid);

        await using var result = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        result.LeaseId.Should().Be("00112233445566778899aabbccddeeff");
    }

    [Fact]
    public async Task should_throw_when_acquire_timeout_is_negative_except_infinite()
    {
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(-5) },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("acquireTimeout");
    }

    [Fact]
    public async Task should_throw_when_acquire_timeout_is_extremely_large()
    {
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions
                {
                    AcquireTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1),
                },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("acquireTimeout");
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
        using var listener = new ActivityListener();
        listener.ShouldListenTo = source =>
            string.Equals(source.Name, "Headless.DistributedLocks", StringComparison.Ordinal);
        listener.Sample = static (ref _) => ActivitySamplingResult.AllData;
        listener.ActivityStopped = activities.Add;
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
    public async Task should_not_expose_handle_lost_token_when_monitoring_is_disabled()
    {
        using var connectionLostCts = new CancellationTokenSource();
        _storage.ConnectionLostToken = connectionLostCts.Token;
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        handle.CanObserveLoss.Should().BeFalse();
        handle.LostToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_expose_handle_lost_token_when_monitoring_is_enabled()
    {
        using var connectionLostCts = new CancellationTokenSource();
        _storage.ConnectionLostToken = connectionLostCts.Token;
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await provider.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { Monitoring = LockMonitoringMode.Monitor },
            AbortToken
        );

        handle.CanObserveLoss.Should().BeTrue();
        handle.LostToken.Should().Be(connectionLostCts.Token);
    }

    [Fact]
    public async Task should_return_lock_info_for_remote_holder_without_local_lease_id()
    {
        var resource = Faker.Random.AlphaNumeric(12);
        var provider = _CreateProvider(
            storage: new InspectableConnectionScopedLockStorage(lockedResource: resource, localLeaseId: null)
        );

        var leaseId = await provider.GetLeaseIdAsync(resource, AbortToken);
        var isLocked = await provider.IsLockedAsync(resource, AbortToken);
        var info = await provider.GetLockInfoAsync(resource, AbortToken);

        leaseId.Should().BeNull();
        isLocked.Should().BeTrue();
        info.Should().NotBeNull();
        info!.Resource.Should().Be(resource);
        info.LeaseId.Should().BeNull();
        info.TimeToLive.Should().BeNull();
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

        var rwProvider = new ConnectionScopedReadWriteLock(_CreateProvider());
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

        var rwProvider = new ConnectionScopedReadWriteLock(_CreateProvider());
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

    private ConnectionScopedDistributedLock _CreateProvider(
        IFencingTokenSource? fencingTokenSource = null,
        TimeSpan? pollingFallback = null,
        DistributedLockOptions? options = null,
        IConnectionScopedLockStorage? storage = null,
        IReleaseSignal? releaseSignal = null
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new ConnectionScopedDistributedLock(
            storage ?? _storage,
            releaseSignal ?? _releaseSignal,
            options ?? new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<ConnectionScopedDistributedLock>(),
            fencingTokenSource,
            pollingFallback
        );
    }

    private sealed class FakeConnectionScopedLockStorage : IConnectionScopedLockStorage
    {
        public Queue<bool> AcquireResults { get; } = new();
        public CancellationToken ConnectionLostToken { get; set; } = CancellationToken.None;

        public TaskCompletionSource? BlockAcquire { get; set; }

        public int AcquireCount { get; private set; }

        public bool BlocksServerSide { get; set; }

        public int ReleaseCount { get; private set; }

        private Dictionary<string, string> LocalLeaseIds { get; } = new(StringComparer.Ordinal);

        public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
            string resource,
            string leaseId,
            bool isShared,
            bool observeLoss,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCount++;

            if (BlockAcquire is not null)
            {
                await BlockAcquire.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var acquired = AcquireResults.Count == 0 || AcquireResults.Dequeue();

            if (!acquired)
            {
                return null;
            }

            LocalLeaseIds[resource] = leaseId;

            return new ConnectionScopedLockHandle(
                resource,
                leaseId,
                ReleaseAsync,
                observeLoss ? ConnectionLostToken : CancellationToken.None
            );
        }

        public ValueTask ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            LocalLeaseIds.Remove(handle.Resource);

            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            LocalLeaseIds.Remove(resource);

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> IsLockedAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(LocalLeaseIds.ContainsKey(resource));
        }

        public ValueTask<long> GetLocksCountAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult((long)(LocalLeaseIds.ContainsKey(resource) ? 1 : 0));
        }

        public ValueTask<string?> GetLocalLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(LocalLeaseIds.TryGetValue(resource, out var leaseId) ? leaseId : null);
        }

        public ValueTask<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<DistributedLockInfo> result = LocalLeaseIds
                .Select(x => new DistributedLockInfo
                {
                    Resource = x.Key,
                    LeaseId = x.Value,
                    TimeToLive = null,
                    FencingToken = null,
                })
                .ToList();

            return ValueTask.FromResult(result);
        }

        public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult((long)LocalLeaseIds.Count);
        }
    }

    private sealed class InspectableConnectionScopedLockStorage(string lockedResource, string? localLeaseId)
        : IConnectionScopedLockStorage
    {
        public ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
            string resource,
            string leaseId,
            bool isShared,
            bool observeLoss,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask ReleaseAsync(
            ConnectionScopedLockHandle handle,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> IsLockedAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(string.Equals(resource, lockedResource, StringComparison.Ordinal));

        public ValueTask<long> GetLocksCountAsync(
            string resource,
            bool? isShared = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(string.Equals(resource, lockedResource, StringComparison.Ordinal) ? 1L : 0L);

        public ValueTask<string?> GetLocalLeaseIdAsync(
            string resource,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                string.Equals(resource, lockedResource, StringComparison.Ordinal) ? localLeaseId : null
            );

        public ValueTask<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<IReadOnlyList<DistributedLockInfo>>([]);

        public ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0L);
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

        public async ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
            string resource,
            string leaseId,
            bool isShared,
            bool observeLoss,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);

            if (!_grant)
            {
                return null;
            }

            // Ownership of the handle transfers to the caller (the lock under test), which
            // releases/disposes it.
            return new ConnectionScopedLockHandle(resource, leaseId, ReleaseAsync, CancellationToken.None);
        }

        public ValueTask ReleaseAsync(
            ConnectionScopedLockHandle handle,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default) =>
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

        public ValueTask<string?> GetLocalLeaseIdAsync(
            string resource,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<string?>(null);

        public ValueTask<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<IReadOnlyList<DistributedLockInfo>>([]);

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
