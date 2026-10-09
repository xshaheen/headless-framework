// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transport;

public sealed class InFlightHandlerTrackerTests : TestBase
{
    [Fact]
    public async Task should_complete_at_once_when_nothing_is_in_flight_even_with_no_budget_left()
    {
        // given
        var tracker = new InFlightHandlerTracker();

        // when
        var drain = async () => await tracker.DrainAsync(TimeSpan.Zero, TimeProvider.System);

        // then
        await drain.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_wait_for_tracked_handlers_before_completing_the_drain()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var handler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Track(handler.Task);

        // when
        var drain = tracker.DrainAsync(TimeSpan.FromSeconds(30), TimeProvider.System);

        // then
        drain.IsCompleted.Should().BeFalse();
        tracker.Count.Should().Be(1);

        handler.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        tracker.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_timeout_when_a_handler_outlives_the_budget()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var tracker = new InFlightHandlerTracker();
        var handler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Track(handler.Task);

        // when
        var drain = tracker.DrainAsync(TimeSpan.FromSeconds(10), timeProvider);
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        // then
        await FluentActions.Awaiting(() => drain).Should().ThrowAsync<TimeoutException>();
        handler.SetResult();
    }

    [Fact]
    public async Task should_throw_timeout_at_once_when_the_budget_is_spent_and_a_handler_still_runs()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var handler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Track(handler.Task);

        // when
        var drain = async () => await tracker.DrainAsync(TimeSpan.Zero, TimeProvider.System);

        // then
        await drain.Should().ThrowAsync<TimeoutException>();
        handler.SetResult();
    }

    [Fact]
    public async Task should_also_wait_for_a_handler_tracked_after_the_drain_began()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Track(first.Task);
        var drain = tracker.DrainAsync(TimeSpan.FromSeconds(30), TimeProvider.System);

        // when — a delivery that passed its shutdown check before the drain starts its handler late
        tracker.Track(late.Task);
        first.SetResult();
        await Task.Delay(50, AbortToken);

        // then
        drain.IsCompleted.Should().BeFalse();

        late.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_surface_a_drained_handler_fault()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var handler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Track(handler.Task);

        // when
        var drain = tracker.DrainAsync(TimeSpan.FromSeconds(30), TimeProvider.System);
        handler.SetException(new InvalidOperationException("boom"));

        // then
        await FluentActions.Awaiting(() => drain).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_refuse_try_track_once_the_drain_began_but_keep_tracking_with_track()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        await tracker.DrainAsync(TimeSpan.FromSeconds(1), TimeProvider.System);
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // when
        var accepted = tracker.TryTrack(refused.Task);
        tracker.Track(tracked.Task);

        // then
        accepted.Should().BeFalse();
        tracker.Count.Should().Be(1);
        tracked.SetResult();
    }

    [Fact]
    public void should_refuse_try_track_after_stop_accepting()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var before = new TaskCompletionSource();
        var after = new TaskCompletionSource();

        // when
        var acceptedBefore = tracker.TryTrack(before.Task);
        tracker.StopAccepting();
        var acceptedAfter = tracker.TryTrack(after.Task);

        // then
        acceptedBefore.Should().BeTrue();
        acceptedAfter.Should().BeFalse();
        tracker.Count.Should().Be(1);
        before.SetResult();
    }

    [Fact]
    public void should_snapshot_only_handlers_whose_tag_matches()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var partitionA = new TaskCompletionSource();
        var partitionB = new TaskCompletionSource();
        var untagged = new TaskCompletionSource();
        tracker.Track(partitionA.Task, "a");
        tracker.Track(partitionB.Task, "b");
        tracker.Track(untagged.Task);

        // when
        var matched = tracker.Snapshot(tag => tag is "a");
        var all = tracker.Snapshot();

        // then
        matched.Should().ContainSingle().Which.Should().BeSameAs(partitionA.Task);
        all.Should().HaveCount(3);
        partitionA.SetResult();
        partitionB.SetResult();
        untagged.SetResult();
    }

    [Fact]
    public async Task should_stop_tracking_a_handler_once_it_completes()
    {
        // given
        var tracker = new InFlightHandlerTracker();
        var handler = new TaskCompletionSource();
        tracker.Track(handler.Task);

        // when
        handler.SetResult();
        await handler.Task;

        // then — removal runs synchronously on completion for a source without async continuations
        tracker.Snapshot().Should().BeEmpty();
        tracker.Count.Should().Be(0);
    }
}
