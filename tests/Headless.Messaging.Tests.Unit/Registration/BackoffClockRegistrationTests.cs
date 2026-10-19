// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Registration;

/// <summary>
/// Messaging's failure waits run on a clock registered apart from the app clock, so a fake app clock never stalls them.
/// </summary>
public sealed class BackoffClockRegistrationTests : TestBase
{
    [Fact]
    public async Task should_run_failure_waits_on_the_system_clock_when_the_app_registers_a_fake_clock()
    {
        // given: a test host that fakes the app clock and never advances it
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        _AddMessaging(services);

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredKeyedService<TimeProvider>(MessagingOptions.BackoffClockServiceKey)
            .Should()
            .BeSameAs(TimeProvider.System);
    }

    [Fact]
    public async Task should_keep_a_backoff_clock_registered_before_messaging()
    {
        // given
        var backoffClock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<TimeProvider>(MessagingOptions.BackoffClockServiceKey, backoffClock);
        _AddMessaging(services);

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredKeyedService<TimeProvider>(MessagingOptions.BackoffClockServiceKey)
            .Should()
            .BeSameAs(backoffClock);
    }

    private static void _AddMessaging(IServiceCollection services)
    {
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });
    }
}
