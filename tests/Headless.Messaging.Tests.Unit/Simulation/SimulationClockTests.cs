// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Simulation;

/// <summary>
/// The simulation clock reports when each wait armed on it is due, and a driver that moves time steps to the nearest
/// one; a reported due time that differs from when the timer actually fires leaves that driver stepping by zero forever.
/// </summary>
public sealed class SimulationClockTests : TestBase
{
    [Fact]
    public async Task should_fire_every_timer_when_stepped_to_its_reported_due_time_after_being_armed_during_advances()
    {
        // given: one thread keeps moving the clock while another arms timers, as a test's wait clock moves time while
        // the code under test arms waits
        const int timerCount = 2_000;
        var clock = new SimulationClock();
        var fired = 0;
        using var stopAdvancing = new CancellationTokenSource();
        var advancing = Task.Run(
            () =>
            {
                while (!stopAdvancing.IsCancellationRequested)
                {
                    clock.Advance(TimeSpan.FromTicks(1));
                }
            },
            AbortToken
        );

        var timers = new List<ITimer>(timerCount);
        for (var i = 0; i < timerCount; i++)
        {
            timers.Add(
                clock.CreateTimer(
                    _ => Interlocked.Increment(ref fired),
                    state: null,
                    TimeSpan.FromSeconds(1),
                    Timeout.InfiniteTimeSpan
                )
            );
        }

        await stopAdvancing.CancelAsync();
        await advancing;

        // when: time moves only to the nearest wait the clock reports, as the wait clock's pump moves it
        var steps = 0;
        for (var pending = clock.PendingWaitsArmedSince(0); pending.Count > 0 && steps < timerCount * 2; steps++)
        {
            clock.Advance(pending[0] > TimeSpan.Zero ? pending[0] : TimeSpan.Zero);
            pending = clock.PendingWaitsArmedSince(0);
        }

        // then
        Volatile.Read(ref fired).Should().Be(timerCount, "a timer is due exactly when the clock reports it due");
        clock.PendingWaitsArmedSince(0).Should().BeEmpty();

        foreach (var timer in timers)
        {
            timer.Dispose();
        }
    }

    [Fact]
    public void should_fire_a_periodic_timer_once_per_period_the_clock_moves_past()
    {
        // given
        var clock = new SimulationClock();
        var fired = 0;
        using var timer = clock.CreateTimer(
            _ => fired++,
            state: null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1)
        );

        // when
        clock.Advance(TimeSpan.FromSeconds(3));

        // then
        fired.Should().Be(3);
        clock.PendingWaitsArmedSince(0).Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void should_stop_reporting_a_timer_once_it_is_disposed_and_refuse_to_rearm_it()
    {
        // given
        var clock = new SimulationClock();
        var timer = clock.CreateTimer(_ => { }, state: null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        // when
        timer.Dispose();

        // then
        clock.PendingWaitsArmedSince(0).Should().BeEmpty();
        timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan).Should().BeFalse();
    }
}
