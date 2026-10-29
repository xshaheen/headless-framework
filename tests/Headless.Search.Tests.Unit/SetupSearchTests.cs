// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Search;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupSearchTests : TestBase
{
    [Fact]
    public void should_throw_naming_use_postgresql_when_no_provider_is_chosen()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessSearch(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Headless.Search*UsePostgreSql*");
    }

    [Fact]
    public void should_throw_when_two_providers_are_chosen()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessSearch(setup =>
            {
                setup.RegisterExtension(new RecordingExtension());
                setup.RegisterExtension(new RecordingExtension());
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    [Fact]
    public void should_add_the_provider_services_once_when_one_provider_is_chosen()
    {
        var services = new ServiceCollection();
        var extension = new RecordingExtension();

        services.AddHeadlessSearch(setup => setup.RegisterExtension(extension));

        extension.Calls.Should().Be(1);
    }

    [Fact]
    public void should_throw_when_search_is_registered_twice()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSearch(setup => setup.RegisterExtension(new RecordingExtension()));

        var act = () => services.AddHeadlessSearch(setup => setup.RegisterExtension(new RecordingExtension()));

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class RecordingExtension : ISearchProviderOptionsExtension
    {
        public int Calls { get; private set; }

        public void AddServices(IServiceCollection services) => Calls++;
    }
}
