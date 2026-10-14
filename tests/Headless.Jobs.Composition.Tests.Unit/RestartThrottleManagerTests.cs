// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class RestartThrottleManagerTests : TestBase
{
    [Fact]
    public void request_restart_debounces_requests_and_invokes_outside_the_manager_lock()
    {
        var timeProvider = new FakeTimeProvider();
        RestartThrottleManager? manager = null;
        var calls = 0;
        manager = new RestartThrottleManager(
            () =>
            {
                calls++;
                manager!.RequestRestart();
            },
            timeProvider
        );
        using (manager)
        {
            manager.RequestRestart();
            manager.RequestRestart();
            timeProvider.Advance(TimeSpan.FromMilliseconds(50));

            calls.Should().Be(1);

            timeProvider.Advance(TimeSpan.FromMilliseconds(50));
            calls.Should().Be(2);
        }
    }

    [Fact]
    public void request_restart_fires_within_the_max_wait_under_a_continuous_request_stream()
    {
        var timeProvider = new FakeTimeProvider();
        var firstRequestAt = timeProvider.GetUtcNow();
        var fireTimes = new List<DateTimeOffset>();
        using var manager = new RestartThrottleManager(() => fireTimes.Add(timeProvider.GetUtcNow()), timeProvider);

        // A request every 40 ms never leaves a 50 ms quiet gap, so a pure debounce would never fire.
        for (var elapsed = 0; elapsed < 1000; elapsed += 40)
        {
            manager.RequestRestart();

            // Advance in 10 ms steps so the fire time is observed at that resolution.
            for (var step = 0; step < 4; step++)
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            }
        }

        fireTimes.Should().NotBeEmpty();
        (fireTimes[0] - firstRequestAt).Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void dispose_before_the_deadline_prevents_the_callback_and_is_idempotent()
    {
        var timeProvider = new FakeTimeProvider();
        var calls = 0;
        var manager = new RestartThrottleManager(() => calls++, timeProvider);
        manager.RequestRestart();

        manager.Dispose();
        manager.Dispose();
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        calls.Should().Be(0);
        var act = manager.RequestRestart;
        act.Should().Throw<ObjectDisposedException>();
    }
}
