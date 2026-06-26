// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UI;
using Microsoft.Extensions.Time.Testing;

namespace Tests.UI;

public sealed class DebounceExtensionsTests : TestBase
{
    [Fact]
    public void debounce_runs_action_once_after_interval_despite_rapid_calls()
    {
        // given
        var clock = new FakeTimeProvider();
        var count = 0;
        Action action = () => count++;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when — three rapid calls within the quiet window
        debounced();
        debounced();
        debounced();

        // then — nothing runs before the interval, then exactly one trailing execution
        count.Should().Be(0);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        count.Should().Be(1);
    }

    [Fact]
    public void debounce_does_not_run_before_interval_elapses()
    {
        // given
        var clock = new FakeTimeProvider();
        var count = 0;
        Action action = () => count++;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when
        debounced();
        clock.Advance(TimeSpan.FromMilliseconds(99));

        // then
        count.Should().Be(0);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        count.Should().Be(1);
    }

    [Fact]
    public void debounce_resets_the_interval_on_each_call()
    {
        // given
        var clock = new FakeTimeProvider();
        var count = 0;
        Action action = () => count++;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when — the second call before the window closes restarts the timer (prior schedule cancelled)
        debounced();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        debounced();
        clock.Advance(TimeSpan.FromMilliseconds(50));

        // then — only 50ms have elapsed since the latest call, so nothing has run yet
        count.Should().Be(0);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        count.Should().Be(1);
    }

    [Fact]
    public void debounce_runs_again_after_a_second_quiet_period()
    {
        // given
        var clock = new FakeTimeProvider();
        var count = 0;
        Action action = () => count++;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when
        debounced();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        debounced();
        clock.Advance(TimeSpan.FromMilliseconds(100));

        // then
        count.Should().Be(2);
    }

    [Fact]
    public void debounce_invokes_action_with_the_latest_arguments()
    {
        // given
        var clock = new FakeTimeProvider();
        var captured = 0;
        Action<int> action = x => captured = x;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when
        debounced(1);
        debounced(2);
        debounced(3);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        // then — only the last call's argument is used
        captured.Should().Be(3);
    }

    [Fact]
    public void debounce_passes_all_arguments_for_multi_arg_overload()
    {
        // given
        var clock = new FakeTimeProvider();
        var sum = 0;
        Action<int, int, int> action = (a, b, c) => sum = a + b + c;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when
        debounced(2, 3, 5);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        // then
        sum.Should().Be(10);
    }

    [Fact]
    public void debounce_routes_action_exception_to_the_on_error_handler()
    {
        // given — the wrapped action throws; the exception must be routed to onError, never out of the
        // thread-pool timer callback (where it would crash the process).
        var clock = new FakeTimeProvider();
        var boom = new InvalidOperationException("boom");
        Action action = () => throw boom;
        Exception? captured = null;
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock, onError: ex => captured = ex);

        // when
        debounced();
        var act = () => clock.Advance(TimeSpan.FromMilliseconds(100));

        // then — the callback does not propagate; onError receives the exception
        act.Should().NotThrow();
        captured.Should().BeSameAs(boom);
    }

    [Fact]
    public void debounce_swallows_action_exception_when_no_on_error_handler_is_supplied()
    {
        // given — without an onError handler a faulting action must be swallowed, not propagated out of the
        // timer callback onto the thread pool.
        var clock = new FakeTimeProvider();
        Action action = () => throw new InvalidOperationException("boom");
        var debounced = action.Debounce(TimeSpan.FromMilliseconds(100), clock);

        // when
        debounced();
        var act = () => clock.Advance(TimeSpan.FromMilliseconds(100));

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void debounce_throws_when_interval_is_zero()
    {
        // given
        Action action = () => { };

        // when
        var act = () => action.Debounce(TimeSpan.Zero);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void debounce_throws_when_interval_is_negative()
    {
        // given
        Action action = () => { };

        // when
        var act = () => action.Debounce(TimeSpan.FromMilliseconds(-1));

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void debounce_throws_when_action_is_null()
    {
        // given
        Action action = null!;

        // when
        var act = () => action.Debounce(TimeSpan.FromMilliseconds(100));

        // then
        act.Should().Throw<ArgumentNullException>();
    }
}
