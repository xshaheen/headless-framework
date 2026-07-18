// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.ReaderWriterLocks;

public sealed class LeaseMonitorIntegrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly InMemoryDistributedReadWriteLockStorage _storage;
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    public LeaseMonitorIntegrationTests()
    {
        _storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
    }

    [Fact]
    public async Task should_renew_read_lease_when_auto_extend_succeeds()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        // when - drive a few cadence intervals; auto-extend should call TryExtendRead which
        // returns true while the reader id is still in the set.
        for (var i = 0; i < 5; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle.RenewalCount >= i + 1, AbortToken);
        }

        // then
        handle.RenewalCount.Should().BeGreaterThanOrEqualTo(3);
        handle.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_signal_lease_loss_for_read_handle_when_auto_extend_returns_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        // when - drop the reader from storage so the next extend returns false AND validate also
        // returns false (no reader id present) — that combination dispatches to Lost.
        await _storage.ReleaseReadAsync($"distributed-lock:{resource}", handle.LeaseId, AbortToken);
        for (var i = 0; i < 10 && !handle.LostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle.LostToken.IsCancellationRequested, AbortToken);
        }

        // then
        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_renew_write_lease_when_auto_extend_succeeds()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        // when
        for (var i = 0; i < 5; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle.RenewalCount >= i + 1, AbortToken);
        }

        // then
        handle.RenewalCount.Should().BeGreaterThanOrEqualTo(3);
        handle.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_signal_lease_loss_for_write_handle_when_auto_extend_returns_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var scopedResource = $"distributed-lock:{resource}";
        await using var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        // when - flip the writer id so extend AND validate both fail -> Lost.
        await _storage.ReleaseWriteAsync(scopedResource, handle.LeaseId, AbortToken);
        await _storage.TryAcquireWriteAsync(
            scopedResource,
            "foreign-writer",
            DistributedLockCoreHelpers.GetWriterWaitingId("foreign-writer"),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(2),
            AbortToken
        );
        for (var i = 0; i < 10 && !handle.LostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle.LostToken.IsCancellationRequested, AbortToken);
        }

        // then
        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_hold_read_handle_when_monitor_only_validate_succeeds()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when - drive a few cadence intervals; validation keeps returning true.
        for (var i = 0; i < 3; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(2));
            await Task.Yield();
        }

        // then - no renewals (monitor-only mode), no loss.
        handle.RenewalCount.Should().Be(0);
        handle.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_signal_lease_loss_for_monitor_only_read_when_validate_returns_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when - flip storage so the validate probe returns false.
        await _storage.ReleaseReadAsync($"distributed-lock:{resource}", handle.LeaseId, AbortToken);
        for (var i = 0; i < 10 && !handle.LostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(3));
            await _DrainUntilAsync(() => handle.LostToken.IsCancellationRequested, AbortToken);
        }

        // then
        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_signal_lease_loss_for_monitor_only_write_when_validate_returns_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var scopedResource = $"distributed-lock:{resource}";
        await using var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when
        await _storage.ReleaseWriteAsync(scopedResource, handle.LeaseId, AbortToken);
        await _storage.TryAcquireWriteAsync(
            scopedResource,
            "foreign-writer",
            DistributedLockCoreHelpers.GetWriterWaitingId("foreign-writer"),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(2),
            AbortToken
        );
        for (var i = 0; i < 10 && !handle.LostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(3));
            await _DrainUntilAsync(() => handle.LostToken.IsCancellationRequested, AbortToken);
        }

        // then
        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_signal_lease_loss_when_storage_throws_transient_exception()
    {
        // given
        var faulty = new TransientFaultStorage();
        var provider = _CreateProvider(faulty);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when - storage throws -> monitor records Unknown for a few cadence ticks without
        // hitting the lease-duration safety net (10s lease, cadence ~5s).
        faulty.FaultProbes = true;
        for (var i = 0; i < 2; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        // then - transient failures alone don't fire LostToken before the safety net.
        handle.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_signal_lease_loss_when_auto_extend_storage_throws_transient_exception()
    {
        // given - AutoExtend path. Renew throws transiently; the handle MUST treat that as
        // Unknown (per-iteration deadline) and let the lease-duration safety net self-promote
        // if failures persist. A single transient blip must NOT fire LostToken.
        var faulty = new TransientFaultStorage();
        var provider = _CreateProvider(faulty);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        // when - storage throws -> monitor records Unknown for a few cadence ticks without
        // hitting the lease-duration safety net (10s lease, cadence ~3.3s).
        faulty.FaultProbes = true;
        for (var i = 0; i < 2; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        // then
        handle.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_coalesce_concurrent_lease_probes_into_single_storage_call()
    {
        // given
        var counting = new CountingStorage();
        var provider = _CreateProvider(counting);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );
        var leaseHandle = (LeaseMonitor.ILeaseHandle)handle;
        counting.ExtendCalls = 0;

        // when - block the inflight probe, then fire two concurrent invocations. Both MUST
        // share the same Task<LeaseState> so the storage call count is exactly one.
        counting.BlockExtend = true;
        var t1 = leaseHandle.RenewOrValidateLeaseAsync(AbortToken);
        var t2 = leaseHandle.RenewOrValidateLeaseAsync(AbortToken);
        counting.ReleaseExtend();
        await Task.WhenAll(t1, t2);

        // then
        counting.ExtendCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_keep_handle_lost_token_none_when_monitoring_is_disabled()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var handle = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        // then - monitoring disabled by default; LostToken stays at CancellationToken.None
        handle.LostToken.Should().Be(CancellationToken.None);
        handle.CanObserveLoss.Should().BeFalse();
    }

    [Fact]
    public async Task should_dispose_handle_without_firing_handle_lost_when_monitoring_is_enabled()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // when - dispose before any failure occurs. LostToken MUST remain unfired because
        // disposal is the consumer-driven shutdown path, not a lease loss.
        await handle.DisposeAsync();

        // then
        handle.LostToken.IsCancellationRequested.Should().BeFalse();
        provider.GetActiveMonitorCount(resource).Should().Be(0);
    }

    private DistributedReadWriteLock _CreateProvider(IDistributedReadWriteLockStorage? storage = null)
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new DistributedReadWriteLock(
            storage ?? _storage,
            outboxBus: null,
            new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedReadWriteLock>()
        );
    }

    private static async Task _DrainUntilAsync(Func<bool> condition, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    private sealed class CountingStorage : IDistributedReadWriteLockStorage
    {
        private readonly InMemoryDistributedReadWriteLockStorage _inner = new(TimeProvider.System);
        private TaskCompletionSource _extendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _extendCalls;

        public int ExtendCalls
        {
            get => _extendCalls;
            set => _extendCalls = value;
        }

        public bool BlockExtend
        {
            get => !_extendGate.Task.IsCompleted;
            set
            {
                if (value)
                {
                    _extendGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                else
                {
                    _extendGate.TrySetResult();
                }
            }
        }

        public void ReleaseExtend()
        {
            _extendGate.TrySetResult();
        }

        public ValueTask<bool> TryAcquireReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.TryAcquireReadAsync(resource, leaseId, ttl, cancellationToken);
        }

        public async ValueTask<bool> TryExtendReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _extendCalls);
            await _extendGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.TryExtendReadAsync(resource, leaseId, ttl, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask ReleaseReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.ReleaseReadAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> TryAcquireWriteAsync(
            string resource,
            string leaseId,
            string waitingId,
            TimeSpan? ttl = null,
            TimeSpan? markerTtl = null,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.TryAcquireWriteAsync(resource, leaseId, waitingId, ttl, markerTtl, cancellationToken);
        }

        public ValueTask<bool> TryExtendWriteAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.TryExtendWriteAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask ReleaseWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.ReleaseWriteAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> ValidateReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.ValidateReadAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> ValidateWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.ValidateWriteAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
        {
            return _inner.IsReadLockedAsync(resource, cancellationToken);
        }

        public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
        {
            return _inner.IsWriteLockedAsync(resource, cancellationToken);
        }

        public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
        {
            return _inner.GetReaderCountAsync(resource, cancellationToken);
        }
    }

    private sealed class TransientFaultStorage : IDistributedReadWriteLockStorage
    {
        private readonly InMemoryDistributedReadWriteLockStorage _inner = new(TimeProvider.System);

        public bool FaultProbes { get; set; }

        public ValueTask<bool> TryAcquireReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.TryAcquireReadAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask<bool> TryExtendReadAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return FaultProbes
                ? ValueTask.FromException<bool>(new TimeoutException("transient"))
                : _inner.TryExtendReadAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask ReleaseReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.ReleaseReadAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> TryAcquireWriteAsync(
            string resource,
            string leaseId,
            string waitingId,
            TimeSpan? ttl = null,
            TimeSpan? markerTtl = null,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.TryAcquireWriteAsync(resource, leaseId, waitingId, ttl, markerTtl, cancellationToken);
        }

        public ValueTask<bool> TryExtendWriteAsync(
            string resource,
            string leaseId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            return FaultProbes
                ? ValueTask.FromException<bool>(new TimeoutException("transient"))
                : _inner.TryExtendWriteAsync(resource, leaseId, ttl, cancellationToken);
        }

        public ValueTask ReleaseWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return _inner.ReleaseWriteAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> ValidateReadAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return FaultProbes
                ? ValueTask.FromException<bool>(new TimeoutException("transient"))
                : _inner.ValidateReadAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> ValidateWriteAsync(
            string resource,
            string leaseId,
            CancellationToken cancellationToken = default
        )
        {
            return FaultProbes
                ? ValueTask.FromException<bool>(new TimeoutException("transient"))
                : _inner.ValidateWriteAsync(resource, leaseId, cancellationToken);
        }

        public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
        {
            return _inner.IsReadLockedAsync(resource, cancellationToken);
        }

        public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
        {
            return _inner.IsWriteLockedAsync(resource, cancellationToken);
        }

        public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
        {
            return _inner.GetReaderCountAsync(resource, cancellationToken);
        }
    }
}
