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

public sealed class DistributedLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly InMemoryDistributedLockStorage _storage;
    private readonly IOutboxBus _outboxBus = Substitute.For<IOutboxBus>();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    public DistributedLockTests()
    {
        _storage = new InMemoryDistributedLockStorage(_timeProvider);
    }

    private DistributedLock _CreateProvider(
        DistributedLockOptions? options = null,
        IDistributedLockStorage? storage = null,
        IOutboxBus? outboxBus = null,
        ILogger<DistributedLock>? logger = null,
        bool useNullOutboxBus = false
    )
    {
        options ??= new DistributedLockOptions();
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());

        return new DistributedLock(
            storage ?? _storage,
            useNullOutboxBus ? null : outboxBus ?? _outboxBus,
            options,
            _guidGenerator,
            _timeProvider,
            logger ?? LoggerFactory.CreateLogger<DistributedLock>()
        );
    }

    [Fact]
    public void should_expose_injected_time_provider()
    {
        var provider = _CreateProvider();

        provider.TimeProvider.Should().BeSameAs(_timeProvider);
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task should_throw_when_resource_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.TryAcquireAsync(null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_resource_is_whitespace()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.TryAcquireAsync("   ", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_resource_exceeds_max_length()
    {
        // given
        var options = new DistributedLockOptions { MaxResourceNameLength = 10 };
        var provider = _CreateProvider(options);
        var longResource = new string('a', 11);

        // when
        var act = async () => await provider.TryAcquireAsync(longResource, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_acquire_timeout_is_negative_except_infinite()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(-5) },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("acquireTimeout");
    }

    [Fact]
    public async Task should_throw_when_acquire_timeout_is_extremely_large()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions
                {
                    AcquireTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1),
                },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("acquireTimeout");
    }

    [Theory]
    [InlineData(LockMonitoringMode.Monitor)]
    [InlineData(LockMonitoringMode.AutoExtend)]
    public async Task should_throw_when_time_until_expires_is_infinite_and_monitoring_requested(LockMonitoringMode mode)
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { TimeUntilExpires = Timeout.InfiniteTimeSpan, Monitoring = mode },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("timeUntilExpires");
    }

    [Fact]
    public async Task should_acquire_lock_when_not_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var guid = new Guid(0x00112233, 0x4455, 0x6677, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff);
        _guidGenerator.Create().Returns(guid);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LeaseId.Should().Be("00112233445566778899aabbccddeeff");
        result.FencingToken.Should().Be(1);
    }

    [Fact]
    public async Task should_issue_monotonic_fencing_tokens_per_resource()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var first = await provider.AcquireAsync(resource, cancellationToken: AbortToken);
        var firstToken = first.FencingToken;
        await first.ReleaseAsync();
        await using var second = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // then
        firstToken.Should().Be(1);
        second.FencingToken.Should().Be(2);
    }

    [Fact]
    public async Task should_allow_protected_resource_to_reject_stale_fencing_token()
    {
        // given
        var provider = _CreateProvider();
        var protectedResource = new FencedProtectedResource();
        var resource = Faker.Random.AlphaNumeric(10);

        await using var first = await provider.AcquireAsync(resource, cancellationToken: AbortToken);
        var staleToken = first.FencingToken;
        protectedResource.TryWrite(staleToken).Should().BeTrue();
        await first.ReleaseAsync();

        await using var second = await provider.AcquireAsync(resource, cancellationToken: AbortToken);
        protectedResource.TryWrite(second.FencingToken).Should().BeTrue();

        // when
        var accepted = protectedResource.TryWrite(staleToken);

        // then
        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_fencing_token_stable_on_renew()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(5) },
            AbortToken
        );

        // when
        var renewed = await handle.RenewAsync(TimeSpan.FromMinutes(5), AbortToken);

        // then
        renewed.Should().BeTrue();
        handle.FencingToken.Should().Be(1);
    }

    [Fact]
    public async Task should_preserve_positional_cancellation_token_argument()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
    }

    [Fact]
    public async Task should_return_null_when_already_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // Acquire first lock
        var firstLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        firstLock.Should().NotBeNull();

        // when - try to acquire second lock with zero timeout (immediate)
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_acquire_lock_with_acquire_async_when_not_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using (var result = await provider.AcquireAsync(resource, cancellationToken: AbortToken))
        {
            // then
            result.Resource.Should().Be(resource);
            result.LeaseId.Should().NotBeNullOrEmpty();
            result.RenewalCount.Should().Be(0);
        }

        // and default releaseOnDispose releases the resource
        await using var reacquired = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        reacquired.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_lock_acquisition_timeout_when_acquire_async_times_out()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var existing = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // when
        var act = async () =>
            await provider.AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

        // then
        var assertion = await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
        assertion.Which.Resource.Should().Be(resource);
    }

    [Fact]
    public async Task should_throw_operation_canceled_when_acquire_async_caller_token_is_cancelled()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await provider.AcquireAsync(resource, cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_operation_canceled_when_acquire_async_wait_is_cancelled()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var existing = await provider.AcquireAsync(resource, cancellationToken: AbortToken);
        using var cts = new CancellationTokenSource();

        // when
        var acquireTask = provider.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            cts.Token
        );
        await Task.Delay(50, AbortToken);
        await cts.CancelAsync();

        // then
        var act = async () => await acquireTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_keep_lock_when_disposed_with_release_on_dispose_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        // when
        await handle.DisposeAsync();

        // then
        (await provider.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeTrue();
        await provider.ReleaseAsync(resource, handle.LeaseId, AbortToken);
    }

    [Fact]
    public async Task should_release_explicitly_when_release_on_dispose_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireAsync(
            resource,
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        // when
        await handle.ReleaseAsync();
        await handle.DisposeAsync();

        // then
        (await provider.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_acquire_lock_when_resource_is_free_and_acquire_timeout_is_zero()
    {
        // Regression guard for issue #282: TimeSpan.Zero must mean "try once with no
        // wait/retry budget", not "fail immediately". On a free resource, the first
        // storage attempt must complete and return a handle. Empirical observation
        // by the messaging integration test author was that the real provider returns
        // null every time under this configuration; see RetryProcessorDistributedLockTests
        // line 101 comment and tests/Headless.Messaging.PostgreSql.Tests.Integration.

        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when - try to acquire on a FREE resource with zero acquire timeout
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // then - handle must be returned; Zero is "no wait", not "no attempt"
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LeaseId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_retry_with_exponential_backoff()
    {
        // given
        var callCount = 0;
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                // Succeed on 3rd attempt
                return ValueTask.FromResult(
                    callCount >= 3
                        ? new DistributedLockAcquireResult(Acquired: true, FencingToken: 1)
                        : DistributedLockAcquireResult.Failed
                );
            });

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // Start acquisition task
        var acquireTask = provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        // Drive the provider's retry loop by advancing fake time until it acquires.
        // A bare `await Task.Yield()` between advances does not reliably drain the
        // CTS-cancellation continuation queued from a prior advance, so we wait for
        // an observable signal (callCount tick) before advancing again.
        for (var advances = 0; advances < 10 && !acquireTask.IsCompleted; advances++)
        {
            var observedBefore = callCount;
            _timeProvider.Advance(TimeSpan.FromMilliseconds(500));

            for (var i = 0; i < 200 && callCount == observedBefore && !acquireTask.IsCompleted; i++)
            {
                await Task.Yield();
            }
        }

        // when
        var result = await acquireTask;

        // then - should have retried and succeeded
        result.Should().NotBeNull();
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task should_return_null_after_acquire_timeout()
    {
        // given
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Pre-lock the resource
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5), AbortToken);

        // when - use zero timeout for immediate failure
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_respect_cancellation_token()
    {
        // given
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Pre-lock the resource
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5), AbortToken);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMinutes(1) },
                cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_use_default_time_until_expires()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        provider.DefaultTimeUntilExpires.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task should_use_custom_time_until_expires()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var customTtl = TimeSpan.FromMinutes(5);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = customTtl },
            AbortToken
        );

        // then
        result.Should().NotBeNull();
        // Verify through observability
        var expiration = await provider.GetExpirationAsync(resource, AbortToken);
        expiration.Should().NotBeNull();
        expiration!.Value.Should().BeCloseTo(customTtl, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_use_infinite_time_until_expires()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = Timeout.InfiniteTimeSpan },
            AbortToken
        );

        // then
        result.Should().NotBeNull();
        // With infinite TTL, expiration should be null
        var expiration = await provider.GetExpirationAsync(resource, AbortToken);
        expiration.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_max_waiters_exceeded()
    {
        // given
        var options = new DistributedLockOptions { MaxWaitersPerResource = 2 };
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);

        // Pre-lock the resource
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5), AbortToken);

        var cts = new CancellationTokenSource();
        try
        {
            // Start multiple waiters - they will wait for retry
#pragma warning disable AsyncFixer04 // Intentionally not awaiting to simulate concurrent waiters
            _ = provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                cts.Token
            );
            await Task.Delay(100, AbortToken); // Give time for waiter1 to enter retry loop

            _ = provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                cts.Token
            );
            await Task.Delay(100, AbortToken); // Give time for waiter2 to enter retry loop
#pragma warning restore AsyncFixer04

            // when - third waiter should throw immediately when max exceeded
            var act = async () =>
                await provider.TryAcquireAsync(
                    resource,
                    new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                    cts.Token
                );

            // then
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Maximum waiters per resource*");
        }
        finally
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    [Fact]
    public async Task should_throw_when_max_concurrent_resources_exceeded()
    {
        // given
        var options = new DistributedLockOptions { MaxConcurrentWaitingResources = 2 };
        var provider = _CreateProvider(options);

        // Pre-lock different resources
        await _storage.InsertAsync(options.KeyPrefix + "resource1", "lock1", TimeSpan.FromMinutes(5), AbortToken);
        await _storage.InsertAsync(options.KeyPrefix + "resource2", "lock2", TimeSpan.FromMinutes(5), AbortToken);
        await _storage.InsertAsync(options.KeyPrefix + "resource3", "lock3", TimeSpan.FromMinutes(5), AbortToken);

        var cts = new CancellationTokenSource();
        try
        {
            // Start waiters on different resources
#pragma warning disable AsyncFixer04 // Intentionally not awaiting to simulate concurrent waiters
            _ = provider.TryAcquireAsync(
                "resource1",
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                cts.Token
            );
            await Task.Delay(100, AbortToken);

            _ = provider.TryAcquireAsync(
                "resource2",
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                cts.Token
            );
            await Task.Delay(100, AbortToken);
#pragma warning restore AsyncFixer04

            // when - third resource should throw
            var act = async () =>
                await provider.TryAcquireAsync(
                    "resource3",
                    new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                    cts.Token
                );

            // then
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*Maximum concurrent waiting resources*");
        }
        finally
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    #region TimeSpan.Zero Safety Deadline Regression Tests

    // Coverage for the Zero-path bypass introduced for issue #297 and the F#2 review
    // finding from PR #284: TryAcquireAsync(acquireTimeout: TimeSpan.Zero) must bound
    // the single storage attempt by an internal safety deadline so that a stalled
    // lock-store call cannot hang the caller indefinitely, even when the caller's
    // CancellationToken does not fire.

    private const int _SafetyDeadlineSeconds = DistributedLockTestSupport.NonBlockingAcquireDeadlineSeconds; // Mirrors _NonBlockingAcquireDeadline.

    private static Func<NSubstitute.Core.CallInfo, ValueTask<DistributedLockAcquireResult>> HangForeverInsert =>
        ci => new ValueTask<DistributedLockAcquireResult>(_HangUntilCancelledAsync(ci.ArgAt<CancellationToken>(3)));

    private static async Task<DistributedLockAcquireResult> _HangUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return DistributedLockAcquireResult.Failed; // Unreachable — Task.Delay throws OperationCanceledException on cancellation.
    }

    private async Task _DrainContinuationsAsync(Task acquireTask)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (!acquireTask.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task should_return_null_when_storage_hangs_and_acquire_timeout_is_zero_and_caller_token_is_none()
    {
        // given — substitute storage that blocks forever on InsertAsync
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(HangForeverInsert);

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when — Zero acquireTimeout, CancellationToken.None (no caller-side bound)
        var acquireTask = provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );

        // Advance past the safety deadline — virtualised via FakeTimeProvider, no wall-clock wait.
        _timeProvider.Advance(TimeSpan.FromSeconds(_SafetyDeadlineSeconds + 1));
        await _DrainContinuationsAsync(acquireTask);

        // then — the call must complete with null rather than hang forever
        acquireTask.IsCompleted.Should().BeTrue("safety deadline must bound the storage call");
        var result = await acquireTask;
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_when_safety_deadline_fires_before_caller_cancellation()
    {
        // given — substitute storage that hangs; caller token has a long-but-finite deadline
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(HangForeverInsert);

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);
        using var callerCts = new CancellationTokenSource();

        // when
        var acquireTask = provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            callerCts.Token
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(_SafetyDeadlineSeconds + 1));
        await _DrainContinuationsAsync(acquireTask);

        // then — safety deadline wins, caller token is unaffected
        var result = await acquireTask;
        result.Should().BeNull();
        callerCts
            .IsCancellationRequested.Should()
            .BeFalse("safety deadline must fire before the caller's much-later deadline");
    }

    // EventId values live in DistributedLockTestSupport (single source across the lock test classes).
    private const int _SafetyDeadlineFiredEventId = DistributedLockTestSupport.SafetyDeadlineFiredEventId;
    private const int _FailedToAcquireLockAfterEventId = DistributedLockTestSupport.FailedToAcquireLockAfterEventId;

    [Fact]
    public async Task should_log_safety_deadline_eventid_and_tag_metric_stalled_when_deadline_fires()
    {
        // given — storage that hangs so the safety deadline (not contention) ends the attempt
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(HangForeverInsert);

        var captured = new List<int>();
        var logger = new DistributedLockTestSupport.CapturingLogger<DistributedLock>(captured);

        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.lock.failed");

        var provider = _CreateProvider(storage: storage, logger: logger);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var acquireTask = provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );
        _timeProvider.Advance(TimeSpan.FromSeconds(_SafetyDeadlineSeconds + 1));
        // Parked wait (not a busy spin): the safety CTS fired by Advance cancels the hung storage
        // call, so the acquire completes promptly. WaitAsync fails fast if it ever hangs.
        (await acquireTask.WaitAsync(TimeSpan.FromSeconds(30), AbortToken))
            .Should()
            .BeNull();

        // then — distinct safety-deadline signal fires; routine-contention signal does NOT.
        // Exclusivity is asserted on the per-provider substitute logger (isolated); the metric
        // assertion only proves the `stalled` tag is wired, since the `headless.lock.failed`
        // instrument is process-wide and shared with parallel tests (incl. the reader/writer lock).
        captured.Should().Contain(_SafetyDeadlineFiredEventId);
        captured.Should().NotContain(_FailedToAcquireLockAfterEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonStalled);
    }

    [Fact]
    public async Task should_log_failed_to_acquire_and_tag_metric_contended_when_resource_is_held()
    {
        // given — storage that promptly returns "not acquired" (routine contention, no stall)
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(DistributedLockAcquireResult.Failed));

        var captured = new List<int>();
        var logger = new DistributedLockTestSupport.CapturingLogger<DistributedLock>(captured);

        using var meterListener = new MeterListener();
        var failedReasons = DistributedLockTestSupport.CaptureFailedReasons(meterListener, "headless.lock.failed");

        var provider = _CreateProvider(storage: storage, logger: logger);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );

        // then — routine contention keeps its existing signal; the safety-deadline EventId stays
        // silent (exclusivity via the isolated substitute logger; metric assertion is Contain-only
        // because the instrument is process-wide).
        result.Should().BeNull();
        captured.Should().Contain(_FailedToAcquireLockAfterEventId);
        captured.Should().NotContain(_SafetyDeadlineFiredEventId);
        failedReasons.Should().Contain(DistributedLockMetrics.ReasonContended);
    }

    [Fact]
    public async Task should_throw_operation_canceled_exception_when_caller_already_cancelled_and_acquire_timeout_is_zero()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        // when — caller token is already cancelled BEFORE the call enters the helper
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                callerCts.Token
            );

        // then — the pre-call ThrowIfCancellationRequested guard fires (no storage call reached)
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_operation_canceled_exception_when_caller_cancels_mid_call_during_acquire_timeout_zero()
    {
        // given — substitute storage that cancels the caller token mid-call (mirrors the existing
        // should_cleanup_orphan_lock_when_acquisition_is_cancelled idiom for the non-Zero path).
        var resource = Faker.Random.AlphaNumeric(10);
        var storage = Substitute.For<IDistributedLockStorage>();
        var provider = _CreateProvider(storage: storage);

        using var callerCts = new CancellationTokenSource();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
#pragma warning disable CA1849, VSTHRD103 // Synchronous Cancel is intentional inside NSubstitute sync callback
                callerCts.Cancel();
#pragma warning restore CA1849, VSTHRD103
                return ValueTask.FromException<DistributedLockAcquireResult>(
                    new OperationCanceledException(callerCts.Token)
                );
            });

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                callerCts.Token
            );

        // then — OCE must propagate to caller, AND best-effort orphan cleanup must fire
        await act.Should().ThrowAsync<OperationCanceledException>();
        await storage
            .Received(1)
            .RemoveIfEqualAsync(
                Arg.Is<string>(s => s.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_call_storage_exactly_once_when_acquire_timeout_is_zero_and_resource_is_held()
    {
        // given — substitute storage that returns false (resource held) on every call
        var callCount = 0;
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
            });

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // then — single attempt, no retry loop entered. Subsequent fake-time advances must not
        // trigger additional storage calls (proves the do-while loop is bypassed).
        result.Should().BeNull();
        callCount.Should().Be(1);

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }

        callCount.Should().Be(1, "Zero-path must not enter the retry/backoff loop");
    }

    [Fact]
    public async Task should_call_orphan_cleanup_when_safety_deadline_fires_during_acquire_timeout_zero()
    {
        // given — storage that hangs InsertAsync until the attempt token fires
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(HangForeverInsert);

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var acquireTask = provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            CancellationToken.None
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(_SafetyDeadlineSeconds + 1));
        await _DrainContinuationsAsync(acquireTask);
        await acquireTask;

        // then — orphan cleanup fires symmetrically with the non-Zero path
        await storage
            .Received(1)
            .RemoveIfEqualAsync(
                Arg.Is<string>(s => s.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_acquire_lock_when_acquire_timeout_is_zero_and_caller_token_is_cancellable()
    {
        // Regression guard: the CanBeCanceled short-circuit (skipping the linked CTS for
        // CancellationToken.None) must not regress the cancellable-caller path. Both paths
        // must succeed when the resource is free.

        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        using var callerCts = new CancellationTokenSource();

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            callerCts.Token
        );

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
    }

    #endregion

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task should_throw_when_release_resource_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.ReleaseAsync(null!, "lock-id", AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_release_lock_id_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.ReleaseAsync("resource", null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("leaseId");
    }

    [Fact]
    public async Task should_release_lock()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        acquiredLock.Should().NotBeNull();

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LeaseId, AbortToken);

        // then
        var isLocked = await provider.IsLockedAsync(resource, AbortToken);
        isLocked.Should().BeFalse();
    }

    [Fact]
    public async Task should_retry_release_on_transient_error()
    {
        // given
        var storage = Substitute.For<IDistributedLockStorage>();
        var callCount = 0;

        storage
            .RemoveIfEqualAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new TimeoutException("Transient error");
                }
                return ValueTask.FromResult(true);
            });

        var provider = new DistributedLock(
            storage,
            _outboxBus,
            new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLock>()
        );

        // when - run release task and advance time through backoff delays
        var releaseTask = provider.ReleaseAsync("resource", "lock-id", AbortToken);

        // Advance time to handle backoff delays
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        await releaseTask;

        // then
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task should_keep_monitor_registered_when_release_fails()
    {
        // given
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(new DistributedLockAcquireResult(Acquired: true, FencingToken: 1));
        storage
            .RemoveIfEqualAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("release failed"));
        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                Monitoring = LockMonitoringMode.Monitor,
                ReleaseOnDispose = false,
            },
            AbortToken
        );
        handle.Should().NotBeNull();

        // when
        var act = async () => await provider.ReleaseAsync(resource, handle!.LeaseId, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        provider.GetActiveMonitorCount(resource).Should().Be(1);

        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task should_publish_lock_released_message()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        acquiredLock.Should().NotBeNull();

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LeaseId, AbortToken);

        // then
        await _outboxBus
            .Received(1)
            .PublishAsync(
                Arg.Is<DistributedLockReleased>(m => m.Resource == resource && m.LeaseId == acquiredLock.LeaseId),
                Arg.Is<PublishOptions?>(options => options == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_release_without_publishing_when_outbox_bus_is_absent()
    {
        // given
        var provider = _CreateProvider(useNullOutboxBus: true);
        var resource = Faker.Random.AlphaNumeric(10);
        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LeaseId, AbortToken);

        // then
        (await provider.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_release_lock_even_when_outbox_publish_fails()
    {
        // given — healthy storage, but the outbox bus dies when publishing the release notification.
        // The publish is a best-effort wake-up for waiters; its failure must not fail the release
        // itself (waiters fall back to polling / TTL expiry).
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        _outboxBus
            .PublishAsync(Arg.Any<DistributedLockReleased>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("outbox down"));

        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        acquiredLock.Should().NotBeNull();

        // when
        var act = async () => await provider.ReleaseAsync(resource, acquiredLock!.LeaseId, AbortToken);

        // then — release completes and the lock is actually gone from storage.
        await act.Should().NotThrowAsync();
        (await provider.IsLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_without_publishing_when_release_exceeds_dispose_timeout()
    {
        // given — storage hangs forever on remove. DisposeTimeout bounds the release so application
        // shutdown is never blocked by sustained storage unavailability; on timeout the release
        // returns without throwing, skips the outbox publish, and the record TTL is the fallback.
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .RemoveIfEqualAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<bool>(new TaskCompletionSource<bool>().Task));

        var options = new DistributedLockOptions { DisposeTimeout = TimeSpan.FromSeconds(5) };
        var provider = _CreateProvider(options, storage: storage);

        // when — run release and advance time past the dispose timeout
        var releaseTask = provider.ReleaseAsync("resource", "lock-id", AbortToken);

        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var act = async () => await releaseTask;

        // then — no throw and no publish (an unconfirmed release must not wake waiters early)
        await act.Should().NotThrowAsync();
        await _outboxBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<DistributedLockReleased>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_acquire_waiting_lock_with_polling_when_outbox_bus_is_absent()
    {
        // given
        var provider = _CreateProvider(useNullOutboxBus: true);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var existing = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // when
        var acquireTask = provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );
        await Task.Delay(50, AbortToken);
        await existing.ReleaseAsync();

        for (var i = 0; i < 20 && !acquireTask.IsCompleted; i++)
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Yield();
        }

        var acquiredLock = await acquireTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        acquiredLock.Should().NotBeNull();
        acquiredLock!.Resource.Should().Be(resource);
    }

    [Fact]
    public void should_log_warning_when_outbox_bus_is_absent()
    {
        // given
        var logger = Substitute.For<ILogger<DistributedLock>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var captured = new List<(LogLevel Level, int Id)>();
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(call => captured.Add((call.Arg<LogLevel>(), call.Arg<EventId>().Id)));

        // when
        _ = _CreateProvider(logger: logger, useNullOutboxBus: true);

        // then
        captured.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Id == 16);
    }

    [Fact]
    public void should_not_log_warning_when_outbox_bus_is_present()
    {
        // given
        var logger = Substitute.For<ILogger<DistributedLock>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var captured = new List<int>();
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(call => captured.Add(call.Arg<EventId>().Id));

        // when
        _ = _CreateProvider(logger: logger);

        // then
        captured.Should().NotContain(16);
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task should_throw_when_renew_resource_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.RenewAsync(null!, "lock-id", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public async Task should_throw_when_renew_lock_id_is_null()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.RenewAsync("resource", null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("leaseId");
    }

    [Fact]
    public async Task should_renew_lock_if_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        acquiredLock.Should().NotBeNull();

        // when
        var result = await provider.RenewAsync(
            resource,
            acquiredLock!.LeaseId,
            timeUntilExpires: TimeSpan.FromMinutes(10),
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_false_if_lock_not_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.RenewAsync(
            resource,
            "non-existent-lock-id",
            timeUntilExpires: TimeSpan.FromMinutes(10),
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_extend_expiration()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(5) },
            AbortToken
        );
        acquiredLock.Should().NotBeNull();

        var expirationBefore = await provider.GetExpirationAsync(resource, AbortToken);

        // when
        await provider.RenewAsync(
            resource,
            acquiredLock!.LeaseId,
            timeUntilExpires: TimeSpan.FromMinutes(30),
            cancellationToken: AbortToken
        );

        // then
        var expirationAfter = await provider.GetExpirationAsync(resource, AbortToken);
        expirationAfter.Should().NotBeNull();
        expirationAfter!.Value.Should().BeGreaterThan(expirationBefore!.Value);
    }

    [Fact]
    public async Task should_cleanup_orphan_lock_when_acquisition_is_cancelled()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var storage = Substitute.For<IDistributedLockStorage>();
        var provider = _CreateProvider(storage: storage);

        using var cts = new CancellationTokenSource();

        // Mock storage to simulate cancellation during InsertAsync. The provider scopes the
        // resource with options.KeyPrefix before calling storage, so the matcher must accept
        // any key (a specific `resource` would never match and the lambda would never run).
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
#pragma warning disable CA1849, VSTHRD103 // Synchronous Cancel is intentional inside NSubstitute sync callback
                cts.Cancel();
#pragma warning restore CA1849, VSTHRD103
                return ValueTask.FromException<DistributedLockAcquireResult>(new OperationCanceledException(cts.Token));
            });

        // when
        var act = async () => await provider.TryAcquireAsync(resource, cancellationToken: cts.Token);

        // then - it must throw because the caller's token was cancelled
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Verify that RemoveIfEqualAsync was called for best-effort cleanup (scoped key).
        await storage
            .Received(1)
            .RemoveIfEqualAsync(
                Arg.Is<string>(s => s.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    #endregion

    #region IsLockedAsync Tests

    [Fact]
    public async Task should_return_false_when_not_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.IsLockedAsync(resource, AbortToken);

        // then
        result.Should().BeFalse();
    }

    #endregion

    #region Observability Tests

    [Fact]
    public async Task should_get_expiration_for_locked_resource()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var ttl = TimeSpan.FromMinutes(10);
        await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = ttl },
            AbortToken
        );

        // when
        var result = await provider.GetExpirationAsync(resource, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().BeCloseTo(ttl, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_return_null_expiration_when_not_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.GetExpirationAsync(resource, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_get_lock_info()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(5) },
            AbortToken
        );

        // when
        var result = await provider.GetLockInfoAsync(resource, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LeaseId.Should().Be(acquiredLock!.LeaseId);
        result.TimeToLive.Should().NotBeNull();
    }

    [Fact]
    public async Task should_return_null_lock_info_when_not_locked()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.GetLockInfoAsync(resource, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_list_active_locks()
    {
        // given
        var provider = _CreateProvider();
        var resources = Enumerable.Range(0, 3).Select(_ => Faker.Random.AlphaNumeric(10)).ToList();

        foreach (var resource in resources)
        {
            await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        }

        // when
        var result = await provider.ListActiveLocksAsync(AbortToken);

        // then
        result.Should().HaveCount(3);
        result.Select(l => l.Resource).Should().BeEquivalentTo(resources);
    }

    [Fact]
    public async Task should_get_active_locks_count()
    {
        // given
        var provider = _CreateProvider();
        for (var i = 0; i < 5; i++)
        {
            var resource = Faker.Random.AlphaNumeric(10);
            await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        }

        // when
        var result = await provider.GetActiveLocksCountAsync(AbortToken);

        // then
        result.Should().Be(5);
    }

    [Fact]
    public async Task should_return_zero_active_locks_count_when_empty()
    {
        // given
        var provider = _CreateProvider();

        // when
        var result = await provider.GetActiveLocksCountAsync(AbortToken);

        // then
        result.Should().Be(0);
    }

    [Fact]
    public async Task should_return_empty_list_when_no_active_locks()
    {
        // given
        var provider = _CreateProvider();

        // when
        var result = await provider.ListActiveLocksAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    #endregion

    private sealed class FencedProtectedResource
    {
        private long _lastSeenFence;

        public bool TryWrite(long? fencingToken)
        {
            if (fencingToken is not { } token || token < _lastSeenFence)
            {
                return false;
            }

            _lastSeenFence = token;

            return true;
        }
    }
}
