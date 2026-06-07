// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

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

    private sealed class FakeCoordinationProviderOptionsExtension : ICoordinationProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) { }
    }
}
