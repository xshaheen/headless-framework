// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

// ReSharper disable AccessToDisposedClosure
public sealed class DistributedLockProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeDistributedLockStorage _storage = new();
    private readonly IOutboxPublisher _outboxPublisher = Substitute.For<IOutboxPublisher>();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();

    private long _lockIdCounter = 1000;

    private DistributedLockProvider _CreateProvider(
        DistributedLockOptions? options = null,
        IDistributedLockStorage? storage = null,
        IOutboxPublisher? outboxPublisher = null,
        ILogger<DistributedLockProvider>? logger = null,
        bool useNullOutboxPublisher = false
    )
    {
        options ??= new DistributedLockOptions();
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new DistributedLockProvider(
            storage ?? _storage,
            useNullOutboxPublisher ? null : outboxPublisher ?? _outboxPublisher,
            options,
            _longIdGenerator,
            _timeProvider,
            logger ?? LoggerFactory.CreateLogger<DistributedLockProvider>()
        );
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
    public async Task should_acquire_lock_when_not_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LockId.Should().NotBeNullOrEmpty();
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
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: AbortToken
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
        await using var result = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // then
        result.Resource.Should().Be(resource);
        result.LockId.Should().NotBeNullOrEmpty();
        result.RenewalCount.Should().Be(0);
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
            await provider.AcquireAsync(resource, acquireTimeout: TimeSpan.Zero, cancellationToken: AbortToken);

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
    public async Task should_keep_lock_when_disposed_with_release_on_dispose_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.AcquireAsync(resource, releaseOnDispose: false, cancellationToken: AbortToken);

        // when
        await handle.DisposeAsync();

        // then
        (await provider.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeTrue();
        await provider.ReleaseAsync(resource, handle.LockId, AbortToken);
    }

    [Fact]
    public async Task should_release_explicitly_when_release_on_dispose_false()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireAsync(
            resource,
            releaseOnDispose: false,
            cancellationToken: AbortToken
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
    public async Task should_acquire_lock_when_resource_is_free_and_acquireTimeout_is_zero()
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
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: AbortToken
        );

        // then - handle must be returned; Zero is "no wait", not "no attempt"
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LockId.Should().NotBeNullOrEmpty();
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
                return ValueTask.FromResult(callCount >= 3);
            });

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // Start acquisition task
        var acquireTask = provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.FromSeconds(30),
            cancellationToken: AbortToken
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
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5));

        // when - use zero timeout for immediate failure
        var result = await provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: AbortToken
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
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await provider.TryAcquireAsync(
                resource,
                acquireTimeout: TimeSpan.FromMinutes(1),
                cancellationToken: cts.Token
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
            timeUntilExpires: customTtl,
            cancellationToken: AbortToken
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
            timeUntilExpires: Timeout.InfiniteTimeSpan,
            cancellationToken: AbortToken
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
        await _storage.InsertAsync(options.KeyPrefix + resource, "existing-lock", TimeSpan.FromMinutes(5));

        var cts = new CancellationTokenSource();
        try
        {
            // Start multiple waiters - they will wait for retry
#pragma warning disable AsyncFixer04 // Intentionally not awaiting to simulate concurrent waiters
            _ = provider.TryAcquireAsync(
                resource,
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken); // Give time for waiter1 to enter retry loop

            _ = provider.TryAcquireAsync(
                resource,
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken); // Give time for waiter2 to enter retry loop
#pragma warning restore AsyncFixer04

            // when - third waiter should throw immediately when max exceeded
            var act = async () =>
                await provider.TryAcquireAsync(
                    resource,
                    acquireTimeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cts.Token
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
        await _storage.InsertAsync(options.KeyPrefix + "resource1", "lock1", TimeSpan.FromMinutes(5));
        await _storage.InsertAsync(options.KeyPrefix + "resource2", "lock2", TimeSpan.FromMinutes(5));
        await _storage.InsertAsync(options.KeyPrefix + "resource3", "lock3", TimeSpan.FromMinutes(5));

        var cts = new CancellationTokenSource();
        try
        {
            // Start waiters on different resources
#pragma warning disable AsyncFixer04 // Intentionally not awaiting to simulate concurrent waiters
            _ = provider.TryAcquireAsync(
                "resource1",
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken);

            _ = provider.TryAcquireAsync(
                "resource2",
                acquireTimeout: TimeSpan.FromSeconds(30),
                cancellationToken: cts.Token
            );
            await Task.Delay(100, AbortToken);
#pragma warning restore AsyncFixer04

            // when - third resource should throw
            var act = async () =>
                await provider.TryAcquireAsync(
                    "resource3",
                    acquireTimeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cts.Token
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

    private const int _SafetyDeadlineSeconds = 10; // Mirrors _NonBlockingAcquireDeadline in DistributedLockProvider.

    private static Func<NSubstitute.Core.CallInfo, ValueTask<bool>> _HangForeverInsert =>
        ci => new ValueTask<bool>(_HangUntilCancelledAsync(ci.ArgAt<CancellationToken>(3)));

    private static async Task<bool> _HangUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return false; // Unreachable — Task.Delay throws OperationCanceledException on cancellation.
    }

    private async Task _DrainContinuationsAsync(Task acquireTask, int maxYields = 500)
    {
        for (var i = 0; i < maxYields && !acquireTask.IsCompleted; i++)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task should_return_null_when_storage_hangs_and_acquireTimeout_is_zero_and_caller_token_is_none()
    {
        // given — substitute storage that blocks forever on InsertAsync
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_HangForeverInsert);

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when — Zero acquireTimeout, CancellationToken.None (no caller-side bound)
        var acquireTask = provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: CancellationToken.None
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
            .Returns(_HangForeverInsert);

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);
        using var callerCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        // when
        var acquireTask = provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: callerCts.Token
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

    [Fact]
    public async Task should_throw_OperationCanceledException_when_caller_already_cancelled_and_acquireTimeout_is_zero()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        // when — caller token is already cancelled BEFORE the call enters the helper
        var act = async () =>
            await provider.TryAcquireAsync(resource, acquireTimeout: TimeSpan.Zero, cancellationToken: callerCts.Token);

        // then — the pre-call ThrowIfCancellationRequested guard fires (no storage call reached)
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_OperationCanceledException_when_caller_cancels_mid_call_during_acquireTimeout_zero()
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
                return ValueTask.FromException<bool>(new OperationCanceledException(callerCts.Token));
            });

        // when
        var act = async () =>
            await provider.TryAcquireAsync(resource, acquireTimeout: TimeSpan.Zero, cancellationToken: callerCts.Token);

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
    public async Task should_call_storage_exactly_once_when_acquireTimeout_is_zero_and_resource_is_held()
    {
        // given — substitute storage that returns false (resource held) on every call
        var callCount = 0;
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.FromResult(false);
            });

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: AbortToken
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
    public async Task should_call_orphan_cleanup_when_safety_deadline_fires_during_acquireTimeout_zero()
    {
        // given — storage that hangs InsertAsync until the attempt token fires
        var storage = Substitute.For<IDistributedLockStorage>();
        storage
            .InsertAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(_HangForeverInsert);

        var provider = _CreateProvider(storage: storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var acquireTask = provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: CancellationToken.None
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
    public async Task should_acquire_lock_when_acquireTimeout_is_zero_and_caller_token_is_cancellable()
    {
        // Regression guard: the CanBeCanceled short-circuit (skipping the linked CTS for
        // CancellationToken.None) must not regress the cancellable-caller path. Both paths
        // must succeed when the resource is free.

        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        using var callerCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        // when
        var result = await provider.TryAcquireAsync(
            resource,
            acquireTimeout: TimeSpan.Zero,
            cancellationToken: callerCts.Token
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
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("lockId");
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
        await provider.ReleaseAsync(resource, acquiredLock!.LockId, AbortToken);

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

        var provider = new DistributedLockProvider(
            storage,
            _outboxPublisher,
            new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
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
    public async Task should_publish_lock_released_message()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);
        acquiredLock.Should().NotBeNull();

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LockId, AbortToken);

        // then
        await _outboxPublisher
            .Received(1)
            .PublishAsync(
                Arg.Is<DistributedLockReleased>(m => m.Resource == resource && m.LockId == acquiredLock.LockId),
                Arg.Is<PublishOptions?>(options => options == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_release_without_publishing_when_outbox_publisher_is_absent()
    {
        // given
        var provider = _CreateProvider(useNullOutboxPublisher: true);
        var resource = Faker.Random.AlphaNumeric(10);
        var acquiredLock = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // when
        await provider.ReleaseAsync(resource, acquiredLock!.LockId, AbortToken);

        // then
        (await provider.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void should_log_warning_when_outbox_publisher_is_absent()
    {
        // given
        var logger = Substitute.For<ILogger<DistributedLockProvider>>();
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
        _ = _CreateProvider(logger: logger, useNullOutboxPublisher: true);

        // then
        captured.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Id == 16);
    }

    [Fact]
    public void should_not_log_warning_when_outbox_publisher_is_present()
    {
        // given
        var logger = Substitute.For<ILogger<DistributedLockProvider>>();
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
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("lockId");
    }

    [Fact]
    public async Task should_renew_lock_if_held()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var acquiredLock = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );
        acquiredLock.Should().NotBeNull();

        // when
        var result = await provider.RenewAsync(
            resource,
            acquiredLock!.LockId,
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
            timeUntilExpires: TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );
        acquiredLock.Should().NotBeNull();

        var expirationBefore = await provider.GetExpirationAsync(resource, AbortToken);

        // when
        await provider.RenewAsync(
            resource,
            acquiredLock!.LockId,
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
                return ValueTask.FromException<bool>(new OperationCanceledException(cts.Token));
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
        await provider.TryAcquireAsync(resource, timeUntilExpires: ttl, cancellationToken: AbortToken);

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
            timeUntilExpires: TimeSpan.FromMinutes(5),
            cancellationToken: AbortToken
        );

        // when
        var result = await provider.GetLockInfoAsync(resource, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Resource.Should().Be(resource);
        result.LockId.Should().Be(acquiredLock!.LockId);
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
}
