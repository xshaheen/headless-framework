// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PushNotificationsSetupBuilderTests
{
    [Fact]
    public void should_register_when_exactly_one_provider_is_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(setup => setup.RegisterExtension(new FakeProviderOptionsExtension()));

        // then
        action.Should().NotThrow();
    }

    [Fact]
    public void should_reject_setup_when_provider_is_missing()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessPushNotifications(static _ => { });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*exactly one provider*");
    }

    [Fact]
    public void should_reject_setup_when_multiple_providers_are_configured()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(setup =>
            {
                setup.RegisterExtension(new FakeProviderOptionsExtension());
                setup.RegisterExtension(new FakeProviderOptionsExtension());
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_reject_repeated_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPushNotifications(setup => setup.RegisterExtension(new FakeProviderOptionsExtension()));

        // when
        var action = () =>
            services.AddHeadlessPushNotifications(setup => setup.RegisterExtension(new FakeProviderOptionsExtension()));

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
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

    private sealed class FakeProviderOptionsExtension : IPushNotificationsProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) { }
    }
}
