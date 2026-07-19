// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.ConnectionScopedLocks;

public sealed class PollingReleaseSignalTests : TestBase
{
    private static readonly TimeSpan _SafetyGuard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_return_when_polling_fallback_elapses_without_a_signal()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        var fallback = TimeSpan.FromSeconds(5);

        var wait = signal.WaitAsync("resource", fallback, AbortToken).AsTask();

        wait.IsCompleted.Should().BeFalse(); // no signal yet, fallback has not elapsed

        timeProvider.Advance(fallback);

        await wait; // completes on the fallback — the correctness floor
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_wake_a_pending_waiter_before_the_fallback_when_published()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);

        // A long fallback means only a real signal — not the clock — can complete this promptly.
        var wait = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), AbortToken).AsTask();

        await signal.PublishAsync("resource", AbortToken);

        await wait.WaitAsync(_SafetyGuard, AbortToken); // completes without advancing the fake clock
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_reuse_a_completed_signal_for_a_later_waiter()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        var fallback = TimeSpan.FromSeconds(5);
        var first = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), AbortToken).AsTask();

        await signal.PublishAsync("resource", AbortToken);
        await first;

        var later = signal.WaitAsync("resource", fallback, AbortToken).AsTask();

        later.IsCompleted.Should().BeFalse();

        timeProvider.Advance(fallback);
        await later;

        later.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_deadlock_when_a_signal_precedes_the_wait()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        var fallback = TimeSpan.FromSeconds(5);

        // Publish with no registered waiter is absorbed; the later wait must still fall back to polling.
        await signal.PublishAsync("resource", AbortToken);

        var wait = signal.WaitAsync("resource", fallback, AbortToken).AsTask();

        wait.IsCompleted.Should().BeFalse(); // the earlier publish was absorbed, not retained

        timeProvider.Advance(fallback);

        await wait; // completes on the fallback rather than hanging
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_the_wait_is_cancelled()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);

        var wait = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), cts.Token).AsTask();

        await cts.CancelAsync();

        var act = async () => await wait;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
