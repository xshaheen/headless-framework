// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Checks that <c>UseFirebase</c> registers <see cref="IFcmPushNotificationService"/> as the same instance as the
/// Firebase <see cref="IPushNotificationService"/>, for the default and for each named instance.
/// </summary>
public sealed class FcmRegistrationTests : TestBase
{
    [Fact]
    public void should_resolve_the_typed_and_shared_default_services_to_one_instance()
    {
        // given
        using var provider = _Build(static setup => setup.UseFirebase(static o => o.Json = "{}"));

        // when
        var typed = provider.GetRequiredService<IFcmPushNotificationService>();
        var shared = provider.GetRequiredService<IPushNotificationService>();

        // then
        typed.Should().BeSameAs(shared);
        provider.GetRequiredService<IFcmPushNotificationService>().Should().BeSameAs(typed, "it is a singleton");
    }

    [Fact]
    public void should_resolve_the_typed_and_shared_named_services_to_one_instance_per_name()
    {
        // given
        using var provider = _Build(static setup =>
        {
            setup.UseFirebase(static o => o.Json = "{}");
            setup.AddNamed("driver", static i => i.UseFirebase(static o => o.Json = "{}"));
            setup.AddNamed("rider", static i => i.UseFirebase(static o => o.Json = "{}"));
        });

        // when
        var driver = provider.GetRequiredKeyedService<IFcmPushNotificationService>("driver");
        var rider = provider.GetRequiredKeyedService<IFcmPushNotificationService>("rider");

        // then
        driver.Should().BeSameAs(provider.GetRequiredKeyedService<IPushNotificationService>("driver"));
        driver.Should().BeSameAs(provider.GetRequiredService<IPushNotificationServiceProvider>().GetService("driver"));
        rider.Should().BeSameAs(provider.GetRequiredKeyedService<IPushNotificationService>("rider"));
        driver.Should().NotBeSameAs(rider);
        driver.Should().NotBeSameAs(provider.GetRequiredService<IFcmPushNotificationService>());
    }

    [Fact]
    public void should_register_no_unkeyed_typed_service_for_a_named_only_host()
    {
        // given
        using var provider = _Build(static setup =>
            setup.AddNamed("driver", static i => i.UseFirebase(static o => o.Json = "{}"))
        );

        // then
        provider.GetService<IFcmPushNotificationService>().Should().BeNull();
        provider.GetKeyedService<IFcmPushNotificationService>("driver").Should().NotBeNull();
    }

    private static ServiceProvider _Build(Action<HeadlessPushNotificationsSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(configure);

        return services.BuildServiceProvider();
    }
}
