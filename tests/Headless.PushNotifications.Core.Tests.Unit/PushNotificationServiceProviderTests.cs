// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PushNotificationServiceProviderTests
{
    private static ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", static instance => instance.UseNoop());
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void get_service_should_resolve_known_name()
    {
        // given
        using var provider = _BuildProvider();
        var serviceProvider = provider.GetRequiredService<IPushNotificationServiceProvider>();

        // when
        var service = serviceProvider.GetService("audit");

        // then - the factory surfaces the same keyed singleton on every call.
        service.Should().BeSameAs(provider.GetRequiredKeyedService<IPushNotificationService>("audit"));
        serviceProvider.GetService("audit").Should().BeSameAs(service);
    }

    [Fact]
    public void get_service_or_null_should_return_null_for_unknown_name()
    {
        // given
        using var provider = _BuildProvider();

        // when / then
        provider.GetRequiredService<IPushNotificationServiceProvider>().GetServiceOrNull("nope").Should().BeNull();
    }

    [Fact]
    public void get_service_should_throw_for_unknown_name_with_message_naming_add_named()
    {
        // given
        using var provider = _BuildProvider();
        var serviceProvider = provider.GetRequiredService<IPushNotificationServiceProvider>();

        // when
        var action = () => serviceProvider.GetService("nope");

        // then - the message points at AddNamed and the provider Use* members.
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No push-notification service is registered under the name 'nope'*")
            .WithMessage("*AddNamed*")
            .WithMessage("*UseFirebase*");
    }

    [Fact]
    public void get_service_should_guard_null_and_whitespace_names()
    {
        // given
        using var provider = _BuildProvider();
        var serviceProvider = provider.GetRequiredService<IPushNotificationServiceProvider>();

        // when / then
        ((Action)(() => serviceProvider.GetService(null!)))
            .Should()
            .Throw<ArgumentException>();
        ((Action)(() => serviceProvider.GetService(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void get_service_or_null_should_guard_null_and_whitespace_names()
    {
        // given
        using var provider = _BuildProvider();
        var serviceProvider = provider.GetRequiredService<IPushNotificationServiceProvider>();

        // when / then
        ((Action)(() => serviceProvider.GetServiceOrNull(null!)))
            .Should()
            .Throw<ArgumentException>();
        ((Action)(() => serviceProvider.GetServiceOrNull(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void default_service_should_not_be_reachable_by_name()
    {
        // given - the default is unkeyed by design; the factory only surfaces named (keyed) instances.
        using var provider = _BuildProvider();

        // when / then
        provider.GetRequiredService<IPushNotificationServiceProvider>().GetServiceOrNull("default").Should().BeNull();
    }

    [Fact]
    public void registered_names_should_list_named_instances_and_exclude_the_default()
    {
        // given
        using var provider = _BuildProvider();

        // when
        var names = provider.GetRequiredService<IPushNotificationServiceProvider>().RegisteredNames;

        // then - use RegisteredNames to validate externally supplied names before resolving.
        names.Should().BeEquivalentTo(["audit"]);
        names.Contains("audit").Should().BeTrue();
        names.Contains("nope").Should().BeFalse();
    }
}
