// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class LeaseLifecycleIntegrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeDistributedLockStorage _storage;

    public LeaseLifecycleIntegrationTests()
    {
        _storage = new FakeDistributedLockStorage(_timeProvider);
    }
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();
    private long _lockIdCounter = 2000;

    [Fact]
    public async Task should_not_create_monitor_by_default()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var handle = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        handle.Should().NotBeNull();
        handle!.HandleLostToken.Should().Be(CancellationToken.None);
        provider.GetActiveMonitorCount(resource).Should().Be(0);
        provider.GetActiveMonitorResourceCount().Should().Be(0);
    }

    [Fact]
    public async Task should_create_monitor_when_monitor_lease_is_enabled_and_deregister_on_dispose()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // then
        handle.Should().NotBeNull();
        handle!.HandleLostToken.Should().NotBe(CancellationToken.None);
        provider.GetActiveMonitorCount(resource).Should().Be(1);
        provider.GetActiveMonitorResourceCount().Should().Be(1);

        await handle.DisposeAsync();
        provider.GetActiveMonitorCount(resource).Should().Be(0);
        provider.GetActiveMonitorResourceCount().Should().Be(0);
    }

    [Fact]
    public async Task should_cancel_handle_lost_token_when_monitored_lock_id_changes()
    {
        // given
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );
        handle.Should().NotBeNull();
        _storage.SetLock(options.KeyPrefix + resource, "foreign-lock", TimeSpan.FromSeconds(10));
        var lostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lostRegistration = handle!
            .HandleLostToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), lostSignal);

        // when
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, "foreign-lock"));
        await lostSignal.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
        stopwatch.Stop();

        // then
        handle!.HandleLostToken.IsCancellationRequested.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task should_auto_promote_monitor_when_auto_extend_is_enabled()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        // then
        handle.Should().NotBeNull();
        provider.GetActiveMonitorCount(resource).Should().Be(1);
        handle!.HandleLostToken.Should().NotBe(CancellationToken.None);
    }

    [Fact]
    public async Task should_cancel_handle_lost_token_after_ttl_without_auto_extend()
    {
        // given - monitor-only mode. After lease TTL elapses without renewal the storage row
        // is gone, so the next probe sees absence (or differing id) and declares Lost.
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );
        handle.Should().NotBeNull();

        // when - advance beyond the real storage TTL and confirm storage expires it.
        (await provider.IsLockedAsync(resource, AbortToken)).Should().BeTrue();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        (await provider.IsLockedAsync(resource, AbortToken)).Should().BeFalse();
        (await provider.GetLockIdAsync(resource, AbortToken)).Should().BeNull();
        (await provider.GetExpirationAsync(resource, AbortToken)).Should().BeNull();

        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await _DrainUntilAsync(() => handle!.HandleLostToken.IsCancellationRequested);

        // then
        handle!.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_empty_monitor_bucket_after_abandoned_handle_is_collected()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await _AcquireMonitoredHandleAndDropReference(provider, resource);

        provider.GetActiveMonitorCount(resource).Should().Be(1);
        provider.GetActiveMonitorResourceCount().Should().Be(1);

        // when
        for (var i = 0; i < 20 && provider.GetActiveMonitorResourceCount() != 0; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            _timeProvider.Advance(TimeSpan.FromSeconds(2));
            await Task.Yield();
        }

        // then
        provider.GetActiveMonitorCount(resource).Should().Be(0);
        provider.GetActiveMonitorResourceCount().Should().Be(0);
    }

    [Fact]
    public async Task should_keep_lock_past_ttl_when_auto_extend_is_enabled()
    {
        // given - auto-extend mode renews via RenewAsync each cadence tick; storage row stays.
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );
        handle.Should().NotBeNull();

        // when - advance past TTL multiple cadence intervals.
        for (var i = 0; i < 4; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        // then - HandleLostToken still not fired (auto-extend kept storage row alive).
        handle!.HandleLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_increment_renewal_count_when_background_auto_extend_renews()
    {
        // given - auto-extend handle: each cadence tick that succeeds calls RenewAsync.
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );
        handle.Should().NotBeNull();

        // when - drive several cadence intervals; background loop should renew.
        for (var i = 0; i < 5; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await _DrainUntilAsync(() => handle!.RenewalCount >= i + 1);
        }

        // then - background renewals were recorded.
        handle!.RenewalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task should_report_null_provider_lock_as_unmonitored()
    {
        // given
        var provider = new NullDistributedLockProvider(_timeProvider);

        // when
        await using var handle = await provider.TryAcquireAsync(
            Faker.Random.AlphaNumeric(10),
            cancellationToken: AbortToken
        );

        // then
        handle!.IsMonitored.Should().BeFalse();
        handle.HandleLostToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_expose_is_monitored_flag()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when - monitor enabled
        await using var monitored = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        // then
        monitored!.IsMonitored.Should().BeTrue();

        await monitored.DisposeAsync();

        // when - monitor disabled
        await using var unmonitored = await provider.TryAcquireAsync(
            Faker.Random.AlphaNumeric(10),
            cancellationToken: AbortToken
        );

        // then
        unmonitored!.IsMonitored.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_deadlock_when_handle_lost_callback_disposes_handle()
    {
        // given - acquire with monitorLease enabled; register a HandleLostToken callback that
        // disposes the handle. Storage row swap triggers Lost on the next probe.
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );
        handle.Should().NotBeNull();
        var disposalDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handle!
            .HandleLostToken.Register(
                () =>
                {
                    _ = Task.Run(async () =>
                    {
                        await handle.DisposeAsync();
                        disposalDone.TrySetResult(true);
                    });
                }
            );
        _storage.SetLock(options.KeyPrefix + resource, "foreign-lock", TimeSpan.FromSeconds(10));

        // when - trigger validation
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, "foreign-lock"));

        // then - dispose path completes in bounded time (no deadlock).
        var work = Task.Run(async () => await disposalDone.Task);
        await work.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_not_signal_handle_lost_on_self_release()
    {
        // given - monitor-backed handle, then provider.ReleaseAsync (direct) instead of dispose.
        // The release path's OnLockReleased nudge must not declare Lost for the same lockId.
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );
        handle.Should().NotBeNull();

        // when - direct release of the same lockId; this triggers the outbox path and would
        // historically nudge the still-alive monitor for that resource.
        await provider.ReleaseAsync(resource, handle!.LockId, AbortToken);
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, handle.LockId));
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        await _DrainUntilAsync(() => provider.GetActiveMonitorCount(resource) == 0);

        // then - HandleLostToken stays unfired after a monitor cadence opportunity.
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
        provider.GetActiveMonitorCount(resource).Should().Be(0);

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_argument_exception_when_monitor_lease_with_infinite_ttl()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var act = () =>
            provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = Timeout.InfiniteTimeSpan,
                    Monitoring = LockMonitoringMode.Monitor,
                },
                AbortToken
            );

        // then
        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be("timeUntilExpires");
    }

    [Fact]
    public async Task should_signal_lease_loss_via_polling_when_outbox_publisher_is_null()
    {
        // AC8 — without an outbox publisher, the only way the monitor can learn of loss is
        // via its own cadence-driven probe. Confirm that polling alone (no nudge) eventually
        // surfaces HandleLostToken when storage is mutated by another party.
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options, outboxBus: null);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );
        handle.Should().NotBeNull();

        // when - foreign party takes over the row; advance the clock past cadence intervals.
        _storage.SetLock(options.KeyPrefix + resource, "foreign-lock", TimeSpan.FromSeconds(10));

        for (var i = 0; i < 10 && !handle!.HandleLostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(6));
            await _DrainUntilAsync(() => handle.HandleLostToken.IsCancellationRequested);
        }

        // then - polling cadence alone (no nudge) was sufficient to detect loss.
        handle!.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    private DistributedLockProvider _CreateProvider(DistributedLockOptions? options = null)
        => _CreateProvider(options, Substitute.For<IOutboxBus>());

    private DistributedLockProvider _CreateProvider(DistributedLockOptions? options, IOutboxBus? outboxBus)
    {
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new DistributedLockProvider(
            _storage,
            outboxBus,
            options ?? new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
        );
    }

    private static async Task _AcquireMonitoredHandleAndDropReference(DistributedLockProvider provider, string resource)
    {
        var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
            }
        );
        handle.Should().NotBeNull();
        handle = null;
        _ = handle;
    }

    private static async Task _DrainUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await Task.Delay(1);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
