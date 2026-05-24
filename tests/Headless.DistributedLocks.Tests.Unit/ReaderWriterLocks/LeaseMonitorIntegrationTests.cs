// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.ReaderWriterLocks;

public sealed class LeaseMonitorIntegrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeReaderWriterLockStorage _storage = new();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();
    private long _lockIdCounter = 7000;

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
            await _DrainUntilAsync(() => handle.RenewalCount >= i + 1);
        }

        // then
        handle.RenewalCount.Should().BeGreaterThanOrEqualTo(3);
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
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
        await _storage.ReleaseReadAsync($"distributed-lock:{resource}", handle.LockId, AbortToken);
        for (var i = 0; i < 10 && !handle.HandleLostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle.HandleLostToken.IsCancellationRequested);
        }

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
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
            await _DrainUntilAsync(() => handle.RenewalCount >= i + 1);
        }

        // then
        handle.RenewalCount.Should().BeGreaterThanOrEqualTo(3);
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
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
        _storage.SetWrite(scopedResource, "foreign-writer");
        for (var i = 0; i < 10 && !handle.HandleLostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle.HandleLostToken.IsCancellationRequested);
        }

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
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
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
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
        await _storage.ReleaseReadAsync($"distributed-lock:{resource}", handle.LockId, AbortToken);
        for (var i = 0; i < 10 && !handle.HandleLostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(3));
            await _DrainUntilAsync(() => handle.HandleLostToken.IsCancellationRequested);
        }

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
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
        _storage.SetWrite(scopedResource, "foreign-writer");
        for (var i = 0; i < 10 && !handle.HandleLostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(3));
            await _DrainUntilAsync(() => handle.HandleLostToken.IsCancellationRequested);
        }

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
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

        // then - transient failures alone don't fire HandleLostToken before the safety net.
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_signal_lease_loss_when_auto_extend_storage_throws_transient_exception()
    {
        // given - AutoExtend path. Renew throws transiently; the handle MUST treat that as
        // Unknown (per-iteration deadline) and let the lease-duration safety net self-promote
        // if failures persist. A single transient blip must NOT fire HandleLostToken.
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
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
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

        // then - monitoring disabled by default; HandleLostToken stays at CancellationToken.None
        handle.HandleLostToken.Should().Be(CancellationToken.None);
        handle.IsMonitored.Should().BeFalse();
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

        // when - dispose before any failure occurs. HandleLostToken MUST remain unfired because
        // disposal is the consumer-driven shutdown path, not a lease loss.
        await handle.DisposeAsync();

        // then
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
        provider.GetActiveMonitorCount(resource).Should().Be(0);
    }

    private DistributedReaderWriterLockProvider _CreateProvider(IDistributedReaderWriterLockStorage? storage = null)
    {
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new DistributedReaderWriterLockProvider(
            storage ?? _storage,
            outboxPublisher: null,
            new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedReaderWriterLockProvider>()
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

    private sealed class CountingStorage : IDistributedReaderWriterLockStorage
    {
        private readonly FakeReaderWriterLockStorage _inner = new();
        private TaskCompletionSource _extendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExtendCalls;

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

        public void ReleaseExtend() => _extendGate.TrySetResult();

        public ValueTask<bool> TryAcquireReadAsync(
            string resource,
            string lockId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        ) => _inner.TryAcquireReadAsync(resource, lockId, ttl, cancellationToken);

        public async ValueTask<bool> TryExtendReadAsync(
            string resource,
            string lockId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref ExtendCalls);
            await _extendGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.TryExtendReadAsync(resource, lockId, ttl, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask ReleaseReadAsync(string resource, string lockId, CancellationToken cancellationToken = default)
            => _inner.ReleaseReadAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> TryAcquireWriteAsync(
            string resource,
            string lockId,
            string waitingId,
            TimeSpan? ttl = null,
            TimeSpan? markerTtl = null,
            CancellationToken cancellationToken = default
        ) => _inner.TryAcquireWriteAsync(resource, lockId, waitingId, ttl, markerTtl, cancellationToken);

        public ValueTask<bool> TryExtendWriteAsync(
            string resource,
            string lockId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        ) => _inner.TryExtendWriteAsync(resource, lockId, ttl, cancellationToken);

        public ValueTask ReleaseWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default)
            => _inner.ReleaseWriteAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> ValidateReadAsync(
            string resource,
            string lockId,
            CancellationToken cancellationToken = default
        ) => _inner.ValidateReadAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> ValidateWriteAsync(
            string resource,
            string lockId,
            CancellationToken cancellationToken = default
        ) => _inner.ValidateWriteAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
            => _inner.IsReadLockedAsync(resource, cancellationToken);

        public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
            => _inner.IsWriteLockedAsync(resource, cancellationToken);

        public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
            => _inner.GetReaderCountAsync(resource, cancellationToken);
    }

    private sealed class TransientFaultStorage : IDistributedReaderWriterLockStorage
    {
        private readonly FakeReaderWriterLockStorage _inner = new();

        public bool FaultProbes { get; set; }

        public ValueTask<bool> TryAcquireReadAsync(
            string resource,
            string lockId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        ) => _inner.TryAcquireReadAsync(resource, lockId, ttl, cancellationToken);

        public ValueTask<bool> TryExtendReadAsync(
            string resource,
            string lockId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        ) => FaultProbes
            ? ValueTask.FromException<bool>(new TimeoutException("transient"))
            : _inner.TryExtendReadAsync(resource, lockId, ttl, cancellationToken);

        public ValueTask ReleaseReadAsync(string resource, string lockId, CancellationToken cancellationToken = default)
            => _inner.ReleaseReadAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> TryAcquireWriteAsync(
            string resource,
            string lockId,
            string waitingId,
            TimeSpan? ttl = null,
            TimeSpan? markerTtl = null,
            CancellationToken cancellationToken = default
        ) => _inner.TryAcquireWriteAsync(resource, lockId, waitingId, ttl, markerTtl, cancellationToken);

        public ValueTask<bool> TryExtendWriteAsync(
            string resource,
            string lockId,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default
        ) => FaultProbes
            ? ValueTask.FromException<bool>(new TimeoutException("transient"))
            : _inner.TryExtendWriteAsync(resource, lockId, ttl, cancellationToken);

        public ValueTask ReleaseWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default)
            => _inner.ReleaseWriteAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> ValidateReadAsync(
            string resource,
            string lockId,
            CancellationToken cancellationToken = default
        ) => FaultProbes
            ? ValueTask.FromException<bool>(new TimeoutException("transient"))
            : _inner.ValidateReadAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> ValidateWriteAsync(
            string resource,
            string lockId,
            CancellationToken cancellationToken = default
        ) => FaultProbes
            ? ValueTask.FromException<bool>(new TimeoutException("transient"))
            : _inner.ValidateWriteAsync(resource, lockId, cancellationToken);

        public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
            => _inner.IsReadLockedAsync(resource, cancellationToken);

        public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
            => _inner.IsWriteLockedAsync(resource, cancellationToken);

        public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
            => _inner.GetReaderCountAsync(resource, cancellationToken);
    }
}
