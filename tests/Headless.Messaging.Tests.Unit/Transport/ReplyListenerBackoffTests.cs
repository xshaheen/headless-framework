// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transport;

/// <summary>
/// A reply listener waits before each reconnect: about one second at first, doubling up to thirty, back to about one
/// second once its channel opens, and jittered so a fleet that loses the broker at once does not reconnect in step.
/// </summary>
public sealed class ReplyListenerBackoffTests : TestBase
{
    private static readonly TimeSpan _Floor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _Cap = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_keep_every_delay_inside_its_jittered_band_and_never_above_the_cap()
    {
        // given
        var clock = new FakeTimeProvider();
        var backoff = new ReplyListenerBackoff(clock, new Random(1234));
        double[] nominalSeconds = [1, 2, 4, 8, 16, 30, 30, 30];

        foreach (var nominal in nominalSeconds.Select(TimeSpan.FromSeconds))
        {
            // then — the floor pins the first delay, so it spreads up from it; later ones spread down from the nominal
            var (lower, upper) = nominal == _Floor ? (_Floor, _Floor * 1.25) : (nominal * 0.75, nominal);
            backoff.Delay.Should().BeGreaterThanOrEqualTo(lower).And.BeLessThanOrEqualTo(upper);
            backoff.Delay.Should().BeGreaterThanOrEqualTo(_Floor).And.BeLessThanOrEqualTo(_Cap);

            // when
            (await _WaitExactlyAsync(clock, backoff))
                .Should()
                .BeTrue();
        }
    }

    [Fact]
    public async Task should_wait_the_low_edge_of_each_band_when_the_jitter_draws_its_minimum()
    {
        // when
        var delays = await _WaitSequenceAsync(drawMaximum: false, count: 7);

        // then
        delays
            .Should()
            .Equal(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1.5),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(6),
                TimeSpan.FromSeconds(12),
                TimeSpan.FromSeconds(22.5),
                TimeSpan.FromSeconds(22.5)
            );
    }

    [Fact]
    public async Task should_wait_up_to_the_high_edge_of_each_band_when_the_jitter_draws_its_maximum()
    {
        // when
        var delays = await _WaitSequenceAsync(drawMaximum: true, count: 7);

        // then — the band's upper edge is exclusive by one millisecond
        var oneMs = TimeSpan.FromMilliseconds(1);
        delays
            .Should()
            .Equal(
                TimeSpan.FromSeconds(1.25) - oneMs,
                TimeSpan.FromSeconds(2) - oneMs,
                TimeSpan.FromSeconds(4) - oneMs,
                TimeSpan.FromSeconds(8) - oneMs,
                TimeSpan.FromSeconds(16) - oneMs,
                _Cap - oneMs,
                _Cap - oneMs
            );
    }

    [Fact]
    public async Task should_start_over_at_the_floor_band_after_a_reset()
    {
        // given — a listener that backed off several times before its channel opened
        var clock = new FakeTimeProvider();
        var backoff = new ReplyListenerBackoff(clock, new PinnedRandom(drawMaximum: false));
        for (var i = 0; i < 4; i++)
        {
            (await _WaitExactlyAsync(clock, backoff)).Should().BeTrue();
        }

        backoff.Delay.Should().Be(TimeSpan.FromSeconds(12));

        // when
        backoff.Reset();

        // then
        backoff.Delay.Should().Be(_Floor);
    }

    [Fact]
    public async Task should_return_false_and_keep_the_delay_when_the_listener_closes_during_the_wait()
    {
        // given
        var clock = new FakeTimeProvider();
        var backoff = new ReplyListenerBackoff(clock, new PinnedRandom(drawMaximum: false));
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var wait = backoff.WaitAsync(closing.Token).AsTask();
        wait.IsCompleted.Should().BeFalse();

        // when
        await closing.CancelAsync();

        // then
        (await wait.WaitAsync(TimeSpan.FromSeconds(10), AbortToken))
            .Should()
            .BeFalse();
        backoff.Delay.Should().Be(_Floor);
    }

    [Theory]
    [InlineData(false, 5000)]
    [InlineData(true, 6249)]
    public void should_spread_up_from_a_floor_above_the_nominal_delay_without_passing_the_cap(
        bool drawMaximum,
        int expectedMilliseconds
    )
    {
        // given — a caller that must wait at least five seconds, as the NATS consumer does after a JetStream API error
        var floor = TimeSpan.FromSeconds(5);

        // when
        var delay = ReconnectBackoff.Jitter(TimeSpan.FromSeconds(2), floor, new PinnedRandom(drawMaximum));

        // then
        delay.Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public void should_never_exceed_the_cap_when_the_floor_sits_at_it()
    {
        // when
        var delay = ReconnectBackoff.Jitter(_Cap, _Cap, new PinnedRandom(drawMaximum: true));

        // then — no room to spread above the cap, so the cap itself
        delay.Should().Be(_Cap);
    }

    // Waits the backoff's current delay on the fake clock and proves the wait ends exactly then, not a tick earlier.
    private static async Task<bool> _WaitExactlyAsync(FakeTimeProvider clock, ReplyListenerBackoff backoff)
    {
        var delay = backoff.Delay;
        var wait = backoff.WaitAsync(AbortToken).AsTask();

        clock.Advance(delay - TimeSpan.FromTicks(1));
        wait.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromTicks(1));

        return await wait.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    // Waits out a fresh backoff's first delays, one after another, and returns each delay it waited.
    private static async Task<List<TimeSpan>> _WaitSequenceAsync(bool drawMaximum, int count)
    {
        var clock = new FakeTimeProvider();
        var backoff = new ReplyListenerBackoff(clock, new PinnedRandom(drawMaximum));
        var delays = new List<TimeSpan>(count);
        for (var i = 0; i < count; i++)
        {
            delays.Add(backoff.Delay);
            (await _WaitExactlyAsync(clock, backoff)).Should().BeTrue();
        }

        return delays;
    }

    /// <summary>Draws the lowest or the highest value of every requested range.</summary>
    private sealed class PinnedRandom(bool drawMaximum) : Random
    {
        public override int Next(int minValue, int maxValue)
        {
            return drawMaximum ? maxValue - 1 : minValue;
        }
    }
}
