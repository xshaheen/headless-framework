// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupDevPushNotificationsTests : TestBase
{
    private static readonly PushNotificationRequest _Request = new() { Title = "t", Body = "b" };

    [Fact]
    public async Task should_share_one_capture_store_between_the_default_and_named_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseDevelopment();
            setup.AddNamed("ios", static instance => instance.UseDevelopment());
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.GetRequiredService<IPushNotificationService>().SendToDeviceAsync("a", _Request, AbortToken);
        await provider
            .GetRequiredService<IPushNotificationServiceProvider>()
            .GetService("ios")
            .SendToDeviceAsync("b", _Request, AbortToken);

        // then
        provider.GetRequiredService<IPushNotificationService>().Should().BeOfType<DevPushNotificationService>();
        var store = provider.GetRequiredService<IPushNotificationCaptureStore>();
        store.Capacity.Should().Be(500);
        store.Query(new()).Select(x => x.SenderName).Should().Equal("ios", "");
    }

    [Fact]
    public void should_size_the_shared_store_from_the_largest_capacity()
    {
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseDevelopment(static options => options.CaptureCapacity = 5);
            setup.AddNamed(
                "ios",
                static instance => instance.UseDevelopment((options, _) => options.CaptureCapacity = 25)
            );
            setup.AddNamed(
                "android",
                static instance => instance.UseDevelopment(static options => options.CaptureCapacity = 1)
            );
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPushNotificationCaptureStore>().Capacity.Should().Be(25);
    }

    [Fact]
    public void should_bind_options_from_configuration()
    {
        // given
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["CaptureCapacity"] = "11" }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseDevelopment((options, _) => options.CaptureCapacity = 2);
            setup.AddNamed("ios", instance => instance.UseDevelopment(config));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IOptionsMonitor<DevPushNotificationOptions>>()
            .Get("ios")
            .CaptureCapacity.Should()
            .Be(11);
        provider.GetRequiredService<IPushNotificationCaptureStore>().Capacity.Should().Be(11);
    }

    [Fact]
    public void should_bind_default_options_from_configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["CaptureCapacity"] = "3" }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(setup => setup.UseDevelopment(config));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPushNotificationCaptureStore>().Capacity.Should().Be(3);
    }

    [Fact]
    public void should_reject_a_zero_capacity()
    {
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(static setup =>
            setup.UseDevelopment(static options => options.CaptureCapacity = 0)
        );
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IPushNotificationCaptureStore>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_register_no_capture_store_for_the_noop_service()
    {
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("ios", static instance => instance.UseNoop());
        });
        using var provider = services.BuildServiceProvider();

        provider.GetService<IPushNotificationCaptureStore>().Should().BeNull();
        provider.GetRequiredService<IPushNotificationService>().Should().BeOfType<NoopPushNotificationService>();
    }
}
