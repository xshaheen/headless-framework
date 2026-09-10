// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.DependencyInjection;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.DependencyInjection;

public sealed class HeadlessRequiredServiceExtensionsTests : TestBase
{
    private const string _Remedy = "Call AddSomething(...) with a provider.";

    [Fact]
    public async Task should_throw_with_the_remedy_when_a_required_service_is_not_registered()
    {
        // given
        var services = new ServiceCollection();
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception.Message.Should().Contain(_Remedy).And.Contain("the marker feature").And.Contain(nameof(IMarker));
        exception.MissingServices.Should().ContainSingle().Which.ServiceType.Should().Be<IMarker>();
    }

    [Fact]
    public async Task should_not_throw_when_the_required_service_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, Marker>();
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_report_every_missing_service_in_a_single_exception()
    {
        // given — a host missing one shared provider would otherwise hit these one restart at a time
        var services = new ServiceCollection();
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);
        services.RequireRegisteredService<IOtherMarker>("the other feature", "Call AddOther(...).");

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception
            .MissingServices.Should()
            .HaveCount(2)
            .And.Contain(missing => missing.RequiredBy == "the marker feature")
            .And.Contain(missing => missing.RequiredBy == "the other feature");
        exception.Message.Should().Contain("the marker feature").And.Contain("the other feature");
    }

    [Fact]
    public async Task should_collapse_identical_requirements_declared_more_than_once()
    {
        // given — the same feature registered twice, or two features needing the same contract with the
        // same wording, must not repeat a line in the failure message
        var services = new ServiceCollection();
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception.MissingServices.Should().ContainSingle();
        exception.Message.Should().Contain("1 required service registration(s)");
    }

    [Fact]
    public async Task should_treat_a_closed_generic_as_present_when_only_the_open_generic_is_registered()
    {
        // given — the shape every caching provider uses: one open-generic registration serves every
        // closed ICache<T> a consuming feature asks for
        var services = new ServiceCollection();
        services.AddSingleton(typeof(IGenericMarker<>), typeof(GenericMarker<>));
        services.RequireRegisteredService<IGenericMarker<Payload>>("the generic feature", _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void should_register_the_startup_check_once_regardless_of_how_many_requirements_are_declared()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);
        services.RequireRegisteredService<IOtherMarker>("the other feature", "Call AddOther(...).");
        services.RequireRegisteredService<IMarker>("the marker feature", _Remedy);

        // then
        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).Should().Be(1);
    }

    private static async Task _RunStartingAsync(IServiceCollection services)
    {
        // The check ships as an internal IHostedLifecycleService, so drive it the way the host does
        // rather than naming the type.
        await using var provider = services.BuildServiceProvider();

        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(AbortToken);
        }
    }

    private interface IMarker;

    private sealed class Marker : IMarker;

    private interface IOtherMarker;

    private interface IGenericMarker<T>;

    private sealed class GenericMarker<T> : IGenericMarker<T>;

    private sealed record Payload;
}
