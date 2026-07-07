// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CoordinationSetupBuilderTests
{
    [Fact]
    public void should_reject_setup_when_provider_is_missing()
    {
        // given
        var services = new ServiceCollection();

        // when
        var action = () => services.AddHeadlessCoordination(static _ => { });

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
            services.AddHeadlessCoordination(setup =>
            {
                setup.RegisterExtension(new FakeCoordinationProviderOptionsExtension());
                setup.RegisterExtension(new FakeCoordinationProviderOptionsExtension());
            });

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_reject_repeated_provider_registration_on_same_service_collection()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessCoordination(setup =>
            setup.RegisterExtension(new FakeCoordinationProviderOptionsExtension())
        );

        // when
        var action = () =>
            services.AddHeadlessCoordination(setup =>
                setup.RegisterExtension(new FakeCoordinationProviderOptionsExtension())
            );

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*Multiple providers*");
    }

    [Fact]
    public void should_use_coordination_membership_when_coordination_is_registered_before_messaging()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // when
        services.AddHeadlessCoordination(setup =>
            setup.RegisterExtension(new FakeCoordinationProviderOptionsExtension())
        );
        services.AddHeadlessMessaging(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<INodeMembership>().Should().BeOfType<MembershipService>();
    }

    [Fact]
    public void should_use_coordination_membership_when_messaging_is_registered_before_coordination()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // when
        services.AddHeadlessMessaging(_ => { });
        services.AddHeadlessCoordination(setup =>
            setup.RegisterExtension(new FakeCoordinationProviderOptionsExtension())
        );
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<INodeMembership>().Should().BeOfType<MembershipService>();
    }

    private sealed class FakeCoordinationProviderOptionsExtension : ICoordinationProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddCoordinationCore<FakeMembershipStore>();
        }
    }
}
