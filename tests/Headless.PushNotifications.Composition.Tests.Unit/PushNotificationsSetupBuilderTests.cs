// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PushNotificationsSetupBuilderTests
{
    [Fact]
    public void should_allow_setup_with_no_default_and_register_provider()
    {
        // given - the default slot is optional (at most one); an empty setup still registers the factory.
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPushNotifications(static _ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IPushNotificationService>().Should().BeNull();
        provider.GetRequiredService<IPushNotificationServiceProvider>().RegisteredNames.Should().BeEmpty();
    }

    [Fact]
    public void should_not_register_default_service_when_named_only_setup()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPushNotifications(static setup =>
            setup.AddNamed("audit", static instance => instance.UseNoop())
        );
        using var provider = services.BuildServiceProvider();

        // then - the named instance resolves while the unkeyed default stays unregistered.
        provider.GetService<IPushNotificationService>().Should().BeNull();
        provider.GetRequiredService<IPushNotificationServiceProvider>().GetService("audit").Should().NotBeNull();
        provider.GetRequiredKeyedService<IPushNotificationService>("audit").Should().NotBeNull();
    }

    [Fact]
    public void should_leave_the_service_collection_unchanged_when_throwing_setup()
    {
        // given - a collection with unrelated registrations plus a setup that fails the at-most-one-default
        // gate even though it queued a named instance.
        var services = new ServiceCollection();
        services.AddLogging();
        var countBefore = services.Count;

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(static setup =>
            {
                setup.UseNoop();
                setup.UseFirebase(static o => o.Json = "{}");
                setup.AddNamed("audit", static instance => instance.UseNoop());
            });

        // then - registration is deferred, so nothing touched the collection.
        action.Should().Throw<InvalidOperationException>().WithMessage("*at most one default*");
        services.Should().HaveCount(countBefore);
        services.Should().NotContain(static d => d.ServiceType == typeof(IPushNotificationService));
        services.Should().NotContain(static d => d.ServiceType == typeof(IPushNotificationServiceProvider));
    }

    [Fact]
    public void should_reject_setup_when_multiple_default_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(static setup =>
            {
                setup.UseNoop();
                setup.UseFirebase(static o => o.Json = "{}");
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*at most one default*")
            .WithMessage("*AddNamed*");
    }

    [Fact]
    public void should_resolve_single_default_service_when_one_provider_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPushNotifications(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetServices<IPushNotificationService>().Should().ContainSingle();
    }

    [Fact]
    public void should_reject_repeated_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(static setup => setup.UseNoop());

        // when
        var action = () => services.AddHeadlessPushNotifications(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_reject_repeated_registration_even_when_first_call_registered_named_instances()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", static instance => instance.UseNoop());
        });

        // when
        var action = () => services.AddHeadlessPushNotifications(static setup => setup.UseNoop());

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*already called on this service collection*");
    }

    [Fact]
    public void should_register_service_provider_as_singleton_when_default_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPushNotifications(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        // then
        var first = provider.GetRequiredService<IPushNotificationServiceProvider>();
        var second = provider.GetRequiredService<IPushNotificationServiceProvider>();
        first.Should().BeSameAs(second);
        first.GetServiceOrNull("anything").Should().BeNull();
    }

    [Fact]
    public void should_be_reachable_via_service_provider_when_named_keyed_registration()
    {
        // given - exercises the provider-agnostic slot mechanics directly through RegisterProvider.
        var namedService = Substitute.For<IPushNotificationService>();
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseNoop();
            setup.AddNamed(
                "marketing",
                instance =>
                {
                    var name = instance.Name;
                    instance.RegisterProvider(svc => svc.AddKeyedSingleton(name, namedService));
                }
            );
        });
        using var provider = services.BuildServiceProvider();

        // when
        var serviceProvider = provider.GetRequiredService<IPushNotificationServiceProvider>();

        // then
        serviceProvider.GetService("marketing").Should().BeSameAs(namedService);
        provider.GetRequiredKeyedService<IPushNotificationService>("marketing").Should().BeSameAs(namedService);
        provider.GetRequiredService<IPushNotificationService>().Should().NotBeSameAs(namedService);
    }

    [Fact]
    public void should_reject_zero_providers_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(static setup =>
            {
                setup.UseNoop();
                setup.AddNamed("marketing", static _ => { });
            });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'marketing' requires exactly one provider*UseFirebase*");
    }

    [Fact]
    public void should_reject_multiple_providers_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(static setup =>
                setup.AddNamed(
                    "marketing",
                    static instance =>
                    {
                        instance.RegisterProvider(static _ => { });
                        instance.RegisterProvider(static _ => { });
                    }
                )
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*'marketing'*");
    }

    [Fact]
    public void should_reject_whitespace_name_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(static setup =>
                setup.AddNamed(" ", static instance => instance.RegisterProvider(static _ => { }))
            );

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_duplicate_names_when_add_named()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(static setup =>
            {
                setup.UseNoop();
                setup.AddNamed("marketing", static instance => instance.RegisterProvider(static _ => { }));
                setup.AddNamed("marketing", static instance => instance.RegisterProvider(static _ => { }));
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*'marketing'*already configured*");
    }

    [Fact]
    public void should_throw_when_configure_is_null()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessPushNotifications(null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }
}
