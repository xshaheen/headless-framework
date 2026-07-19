// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RegularLocks;

public sealed class DistributedSemaphoreProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly InMemoryDistributedSemaphoreStorage _storage;
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    public DistributedSemaphoreProviderTests()
    {
        _storage = new InMemoryDistributedSemaphoreStorage(_timeProvider);
    }

    [Fact]
    public void should_throw_when_max_count_is_less_than_one()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = () => provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), 0);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxCount");
    }

    [Fact]
    public async Task should_allow_up_to_max_count_concurrent_holders()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 2);
        var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero };

        // when
        await using var first = await semaphore.TryAcquireAsync(options, AbortToken);
        await using var second = await semaphore.TryAcquireAsync(options, AbortToken);
        var third = await semaphore.TryAcquireAsync(options, AbortToken);

        // then
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        third.Should().BeNull();
    }

    [Fact]
    public async Task should_issue_guid_formatted_lease_id()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        _guidGenerator.Create().Returns(guid);

        // when
        await using var result = await semaphore.AcquireAsync(cancellationToken: AbortToken);

        // then
        result.LeaseId.Should().Be("00112233445566778899aabbccddeeff");
    }

    [Fact]
    public async Task should_reacquire_after_release_and_advance_fencing_token()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero };

        // when
        await using var first = await semaphore.AcquireAsync(options, AbortToken);
        await first.ReleaseAsync();
        await using var second = await semaphore.AcquireAsync(options, AbortToken);

        // then
        first.FencingToken.Should().Be(1);
        second.FencingToken.Should().Be(2);
    }

    [Fact]
    public async Task should_report_holder_count()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var semaphore = provider.CreateSemaphore(resource, maxCount: 2);

        // when
        await using var first = await semaphore.AcquireAsync(cancellationToken: AbortToken);
        await using var second = await semaphore.AcquireAsync(cancellationToken: AbortToken);
        var count = await provider.GetHolderCountAsync(resource, AbortToken);

        // then
        count.Should().Be(2);
        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task should_reacquire_after_expiry()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        var options = new DistributedLockAcquireOptions
        {
            AcquireTimeout = TimeSpan.Zero,
            TimeUntilExpires = TimeSpan.FromSeconds(10),
        };
        await using var first = await semaphore.AcquireAsync(options, AbortToken);

        // when
        _timeProvider.Advance(TimeSpan.FromSeconds(11));
        await using var second = await semaphore.TryAcquireAsync(options, AbortToken);

        // then
        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task should_renew_slot_without_changing_fencing_token()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        await using var slot = await semaphore.AcquireAsync(cancellationToken: AbortToken);

        // when
        var renewed = await slot.RenewAsync(TimeSpan.FromMinutes(5), AbortToken);

        // then
        renewed.Should().BeTrue();
        slot.FencingToken.Should().Be(1);
    }

    [Fact]
    public async Task should_fire_handle_lost_token_when_lease_is_lost_in_poll_mode()
    {
        // given — acquire with Monitor mode; storage will expire the slot when time advances
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        await using var slot = await semaphore.TryAcquireAsync(
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(2),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );
        slot.Should().NotBeNull();
        slot!.LostToken.Should().NotBe(CancellationToken.None);

        // when — advance the fake clock past TTL so storage evicts the holder entry
        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        // drive multiple cadence intervals so the monitor probe fires
        for (var i = 0; i < 10 && !slot.LostToken.IsCancellationRequested; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(2));
            await _DrainUntilAsync(() => slot.LostToken.IsCancellationRequested);
        }

        // then
        slot.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_retain_slot_beyond_ttl_when_auto_extend_mode()
    {
        // given — acquire with AutoExtend; each cadence tick renews the storage entry
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        await using var slot = await semaphore.TryAcquireAsync(
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(3),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );
        slot.Should().NotBeNull();

        // when — advance past TTL multiple cadence intervals; auto-extend should renew
        for (var i = 0; i < 4; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        // then — LostToken NOT fired; slot is still valid
        slot!.LostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task should_wake_waiting_acquirer_on_push_lock_released_signal()
    {
        // given — fill the semaphore (capacity 1), then start a waiter with a longer timeout
        var resource = Faker.Random.AlphaNumeric(10);
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(resource, maxCount: 1);

        await using var holder = await semaphore.TryAcquireAsync(
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        holder.Should().NotBeNull();

        // start a waiter that will block until a slot frees up
        using var waiterCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiterTask = semaphore.TryAcquireAsync(
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(4) },
            waiterCts.Token
        );

        // when — release the slot and immediately send the push wake-up signal
        await holder.ReleaseAsync();
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, holder.LeaseId));

        // then — waiter is unblocked faster than the polling budget
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await using var second = await waiterTask;
        stopwatch.Stop();

        second.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task should_make_at_least_one_storage_attempt_when_acquire_timeout_is_sub_millisecond()
    {
        // given — sub-millisecond timeout; the first-attempt guard ensures one real storage call
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var semaphore = provider.CreateSemaphore(resource, maxCount: 1);
        var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromTicks(1) };

        // when — even with a near-zero timeout, acquisition should succeed on an empty semaphore
        await using var slot = await semaphore.TryAcquireAsync(options, AbortToken);

        // then — at least one storage attempt was made (we obtained the slot)
        slot.Should().NotBeNull();
        (await provider.GetHolderCountAsync(resource, AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_throw_argument_exception_when_time_until_expires_is_infinite()
    {
        // given — a semaphore slot is a ZSET member scored by a finite expiry; an infinite lease has
        // no score to plant and would hold capacity forever, so it must be rejected up front.
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        var options = new DistributedLockAcquireOptions { TimeUntilExpires = Timeout.InfiniteTimeSpan };

        // when
        var act = async () => await semaphore.AcquireAsync(options, AbortToken);

        // then
        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName("acquireOptions");
    }

    [Fact]
    public async Task should_log_safety_deadline_eventid_and_tag_metric_stalled_when_deadline_fires()
    {
        // given — storage that hangs so the non-blocking safety deadline (not contention) ends the attempt
        var storage = Substitute.For<IDistributedSemaphoreStorage>();
        storage
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => new ValueTask<DistributedLockAcquireResult>(
                _HangUntilCancelledAsync(ci.ArgAt<CancellationToken>(4))
            ));

        // DistributedSemaphoreProvider is internal, so NSubstitute cannot proxy
        // ILogger<DistributedSemaphoreProvider> (Castle DynamicProxy has no access to the internal
        // type arg). Use a real capturing logger instead.
        var captured = new List<int>();
        var logger = new DistributedLockTestSupport.CapturingLogger<DistributedSemaphoreProvider>(captured);

        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.semaphore.failed");

        var semaphore = _CreateSemaphoreWithLogger(storage, logger);

        // when
        var acquireTask = semaphore.TryAcquireAsync(
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );
        _timeProvider.Advance(TimeSpan.FromSeconds(DistributedLockTestSupport.NonBlockingAcquireDeadlineSeconds + 1));
        // Parked wait (not a busy spin): the safety CTS fired by Advance cancels the hung storage
        // call, so the acquire completes promptly. WaitAsync fails fast if it ever hangs.
        (await acquireTask.WaitAsync(TimeSpan.FromSeconds(30), AbortToken))
            .Should()
            .BeNull();

        // then — log EventId proves the signal fires (isolated capturing logger); the metric
        // assertion is Contain-only because `headless.semaphore.failed` is process-wide.
        captured.Should().Contain(DistributedLockTestSupport.SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonStalled);
    }

    [Fact]
    public async Task should_not_log_safety_deadline_eventid_and_tag_metric_contended_when_slot_is_held()
    {
        // given — storage that promptly returns "not acquired" (routine contention, no stall)
        var storage = Substitute.For<IDistributedSemaphoreStorage>();
        storage
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<DistributedLockAcquireResult>(DistributedLockAcquireResult.Failed));

        var captured = new List<int>();
        var logger = new DistributedLockTestSupport.CapturingLogger<DistributedSemaphoreProvider>(captured);
        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.semaphore.failed");
        var semaphore = _CreateSemaphoreWithLogger(storage, logger);

        // when
        var result = await semaphore.TryAcquireAsync(
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );

        // then — routine contention tags `contended`; the safety-deadline EventId stays silent
        result.Should().BeNull();
        captured.Should().NotContain(DistributedLockTestSupport.SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonContended);
    }

    private IDistributedSemaphore _CreateSemaphoreWithLogger(
        IDistributedSemaphoreStorage storage,
        ILogger<DistributedSemaphoreProvider> logger
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var provider = new DistributedSemaphoreProvider(
            storage,
            Substitute.For<IOutboxBus>(),
            new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            logger
        );
        return provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
    }

    private static async Task<DistributedLockAcquireResult> _HangUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return DistributedLockAcquireResult.Failed; // Unreachable — Task.Delay throws on cancellation.
    }

    [Fact]
    public async Task should_return_without_publishing_when_slot_release_exceeds_dispose_timeout()
    {
        // given — slot storage hangs forever on release. DisposeTimeout bounds the release so
        // shutdown is never blocked; on timeout the release returns without throwing, skips the
        // outbox publish, and the slot's TTL is the fallback.
        var hangingStorage = Substitute.For<IDistributedSemaphoreStorage>();
        hangingStorage
            .ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<bool>(new TaskCompletionSource<bool>().Task));
        var outboxBus = Substitute.For<IOutboxBus>();

        var provider = new DistributedSemaphoreProvider(
            hangingStorage,
            outboxBus,
            new DistributedLockOptions { DisposeTimeout = TimeSpan.FromSeconds(5) },
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedSemaphoreProvider>()
        );

        // when — run release and advance time past the dispose timeout
        var releaseTask = provider.ReleaseAsync("resource", "slot-lease", AbortToken);

        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var act = async () => await releaseTask;

        // then
        await act.Should().NotThrowAsync();
        await outboxBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<DistributedLockReleased>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>());
    }

    private DistributedSemaphoreProvider _CreateProvider(DistributedLockOptions? options = null)
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new DistributedSemaphoreProvider(
            _storage,
            Substitute.For<IOutboxBus>(),
            options ?? new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedSemaphoreProvider>()
        );
    }

    private static async Task _DrainUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1));
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
