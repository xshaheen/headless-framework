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
