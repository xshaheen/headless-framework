// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RegularLocks;

public sealed class DisposableSemaphoreSlotTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IDistributedSemaphoreStorage _storage = Substitute.For<IDistributedSemaphoreStorage>();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    public DisposableSemaphoreSlotTests()
    {
        // Default: acquire succeeds, renew succeeds, validate holds, release succeeds.
        _storage
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new DistributedLockAcquireResult(Acquired: true, FencingToken: 1));
        _storage
            .TryExtendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _storage.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _storage.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    // -----------------------------------------------------------------------
    // Idempotent release
    // -----------------------------------------------------------------------

    [Fact]
    public async Task should_release_storage_only_once_when_release_async_called_twice()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource);

        // when
        await slot.ReleaseAsync();
        await slot.ReleaseAsync();

        // then — provider.ReleaseAsync should have been called exactly once
        await _storage
            .Received(1)
            .ReleaseAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_release_storage_only_once_when_dispose_called_twice()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource);

        // when
        await slot.DisposeAsync();
        await slot.DisposeAsync();

        // then
        await _storage
            .Received(1)
            .ReleaseAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_release_storage_only_once_when_release_then_dispose()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource);

        // when
        await slot.ReleaseAsync();
        await slot.DisposeAsync();

        // then
        await _storage
            .Received(1)
            .ReleaseAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    // -----------------------------------------------------------------------
    // DisposeAsync releases the slot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task should_release_on_dispose_when_release_on_dispose_is_true()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource, releaseOnDispose: true);

        // when
        await slot.DisposeAsync();

        // then
        await _storage
            .Received(1)
            .ReleaseAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_release_on_dispose_when_release_on_dispose_is_false()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource, releaseOnDispose: false);

        // when
        await slot.DisposeAsync();

        // then
        await _storage.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Release-ordering fix: transient storage failure allows retry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task should_retry_storage_release_when_first_attempt_throws()
    {
        // given — storage throws on first call, succeeds on second
        var resource = Faker.Random.AlphaNumeric(10);
        var callCount = 0;
        _storage
            .ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    throw new InvalidOperationException("transient storage failure");
                }

                return ValueTask.FromResult(true);
            });

        var slot = await _AcquireSlotAsync(resource);

        // first attempt: storage throws; _isReleased must stay false so we can retry
        try
        {
            await slot.ReleaseAsync();
        }
        catch (InvalidOperationException)
        {
            // expected on first attempt
        }

        // when — second attempt: storage succeeds
        await slot.ReleaseAsync();

        // then — storage was called exactly twice (one failure + one success)
        callCount.Should().Be(2);
        await _storage.Received(2).ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_via_dispose_when_first_release_throws()
    {
        // given — storage throws on first call, succeeds on second
        var resource = Faker.Random.AlphaNumeric(10);
        var callCount = 0;
        _storage
            .ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    throw new InvalidOperationException("transient storage failure");
                }

                return ValueTask.FromResult(true);
            });

        var slot = await _AcquireSlotAsync(resource);

        // first attempt via ReleaseAsync throws; _isReleased must stay false
        try
        {
            await slot.ReleaseAsync();
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // when — retry via DisposeAsync (which calls ReleaseAsync internally)
        await slot.DisposeAsync();

        // then — storage was called twice (failure + success)
        callCount.Should().Be(2);
        await _storage.Received(2).ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // RenewOrValidateLeaseAsync routing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task should_route_auto_extend_lease_validation_to_extend()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)slot).RenewOrValidateLeaseAsync(AbortToken);

        // then — auto-extend calls TryExtendAsync (renew path), not ValidateAsync
        result.Should().Be(LeaseMonitor.LeaseState.Renewed);
        await _storage
            .Received(1)
            .TryExtendAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            );
        await _storage
            .DidNotReceive()
            .ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_route_monitor_only_lease_validation_to_validate()
    {
        // given — autoExtend = false → monitor-only mode uses ValidateAsync
        var resource = Faker.Random.AlphaNumeric(10);
        var slot = await _AcquireSlotAsync(resource, autoExtend: false);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)slot).RenewOrValidateLeaseAsync(AbortToken);

        // then — monitor-only calls ValidateAsync, not TryExtendAsync
        result.Should().Be(LeaseMonitor.LeaseState.Held);
        await _storage
            .Received(1)
            .ValidateAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await _storage
            .DidNotReceive()
            .TryExtendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_treat_renew_false_with_owning_validate_as_unknown_under_auto_extend()
    {
        // given — auto-extend slot whose renew (TryExtendAsync) returns false (transient
        // retry-exhaustion), but the ownership probe (ValidateAsync) still confirms we own the slot.
        var resource = Faker.Random.AlphaNumeric(10);
        _storage
            .TryExtendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _storage.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var slot = await _AcquireSlotAsync(resource, autoExtend: true);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)slot).RenewOrValidateLeaseAsync(AbortToken);

        // then — still owner: classify as Unknown so the safety net governs, not Lost; the handle is
        // not signalled lost.
        result.Should().Be(LeaseMonitor.LeaseState.Unknown);
        slot.LostToken.IsCancellationRequested.Should().BeFalse();
        await _storage
            .Received(1)
            .ValidateAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_return_lost_when_validate_returns_false_in_monitor_mode()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        _storage.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var slot = await _AcquireSlotAsync(resource, autoExtend: false);

        // when
        var result = await ((LeaseMonitor.ILeaseHandle)slot).RenewOrValidateLeaseAsync(AbortToken);

        // then
        result.Should().Be(LeaseMonitor.LeaseState.Lost);
    }

    // -----------------------------------------------------------------------
    // Monitor stop on dispose does not fire LostToken
    // -----------------------------------------------------------------------

    [Fact]
    public async Task should_stop_monitor_on_dispose_without_signaling_loss()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var deregisterCount = 0;
        var slot = await _AcquireSlotAsync(
            resource,
            deregisterMonitor: (_, _) => Interlocked.Increment(ref deregisterCount)
        );
        var monitor = new LeaseMonitor(slot, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
        slot.AttachMonitor(monitor);
        slot.CanObserveLoss.Should().BeTrue();
        var lostToken = slot.LostToken;

        // when
        await slot.DisposeAsync();
        _timeProvider.Advance(TimeSpan.FromSeconds(20));

        // then — monitor is stopped but LostToken is not cancelled
        slot.CanObserveLoss.Should().BeFalse();
        monitor.MonitoringTask.IsCompleted.Should().BeTrue();
        lostToken.IsCancellationRequested.Should().BeFalse();
        deregisterCount.Should().Be(1);
    }

    [Fact]
    public async Task should_stop_monitor_on_explicit_release_without_signaling_loss()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var deregisterCount = 0;
        var slot = await _AcquireSlotAsync(
            resource,
            deregisterMonitor: (_, _) => Interlocked.Increment(ref deregisterCount)
        );
        var monitor = new LeaseMonitor(slot, _timeProvider, LoggerFactory.CreateLogger(nameof(LeaseMonitor)));
        slot.AttachMonitor(monitor);
        var lostToken = slot.LostToken;

        // when
        await slot.ReleaseAsync();
        _timeProvider.Advance(TimeSpan.FromSeconds(20));

        // then
        slot.CanObserveLoss.Should().BeFalse();
        monitor.MonitoringTask.IsCompleted.Should().BeTrue();
        lostToken.IsCancellationRequested.Should().BeFalse();
        deregisterCount.Should().Be(1);
        await _storage
            .Received(1)
            .ReleaseAsync(
                Arg.Is<string>(r => r.EndsWith(resource, StringComparison.Ordinal)),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<DisposableSemaphoreSlot> _AcquireSlotAsync(
        string resource,
        bool releaseOnDispose = true,
        bool autoExtend = false,
        Action<string, string>? deregisterMonitor = null
    )
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());

        var provider = new DistributedSemaphoreProvider(
            _storage,
            Substitute.For<IOutboxBus>(),
            new DistributedLockOptions(),
            _guidGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedSemaphoreProvider>()
        );

        var semaphore = provider.CreateSemaphore(resource, maxCount: 1);
        var monitoring = autoExtend ? LockMonitoringMode.AutoExtend : LockMonitoringMode.None;
        var acquireOptions = new DistributedLockAcquireOptions
        {
            AcquireTimeout = TimeSpan.Zero,
            ReleaseOnDispose = releaseOnDispose,
            Monitoring = monitoring,
        };

        var handle = await semaphore.TryAcquireAsync(acquireOptions, AbortToken);
        handle.Should().NotBeNull("pre-condition: slot must be acquired");

        var slot = (DisposableSemaphoreSlot)handle;

        // Reattach deregisterMonitor if provided by rebuilding the slot directly.
        // DistributedSemaphoreProvider wires its own deregister; for tests that need a custom
        // deregister callback we construct the slot manually.
        if (deregisterMonitor is not null)
        {
            return new DisposableSemaphoreSlot(
                resource,
                slot.LeaseId,
                slot.FencingToken,
                TimeSpan.FromSeconds(10),
                slot.TimeWaitedForLock,
                provider,
                releaseOnDispose,
                autoExtend,
                new DistributedLockOptions(),
                _timeProvider,
                deregisterMonitor,
                LoggerFactory.CreateLogger(nameof(DisposableSemaphoreSlot))
            );
        }

        return slot;
    }
}
