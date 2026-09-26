// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.DependencyInjection;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.DependencyInjection;

public sealed class HeadlessRequiredSingletonServiceExtensionsTests : TestBase
{
    private const string _RequiredBy = "the marker store";
    private const string _Remedy = "Register IMarker as a singleton.";

    [Fact]
    public async Task should_not_throw_when_the_service_is_a_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, Marker>();
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task should_throw_naming_the_requirement_when_the_service_is_not_a_singleton(ServiceLifetime lifetime)
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(IMarker), typeof(Marker), lifetime));
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<InvalidServiceLifetimeException>()).Which;
        exception
            .Message.Should()
            .Contain(nameof(IMarker))
            .And.Contain(_RequiredBy)
            .And.Contain(_Remedy)
            .And.Contain(lifetime.ToString());
        var violation = exception.Violations.Should().ContainSingle().Which;
        violation.Requirement.ServiceType.Should().Be<IMarker>();
        violation.Lifetime.Should().Be(lifetime);
    }

    [Fact]
    public async Task should_read_registrations_added_after_the_requirement()
    {
        // given — the store declares its requirement before the application registers the service
        var services = new ServiceCollection();
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);
        services.AddScoped<IMarker, Marker>();

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().ThrowAsync<InvalidServiceLifetimeException>();
    }

    [Fact]
    public async Task should_judge_the_last_registration_when_a_scoped_one_replaces_a_singleton()
    {
        // given — the container resolves the last registration, so that is the lifetime the store captures
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, Marker>();
        services.AddScoped<IMarker, Marker>();
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().ThrowAsync<InvalidServiceLifetimeException>();
    }

    [Fact]
    public async Task should_judge_the_last_registration_when_a_singleton_replaces_a_scoped_one()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<IMarker, Marker>();
        services.AddSingleton<IMarker, Marker>();
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_judge_the_open_generic_registration_when_no_closed_one_exists()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped(typeof(IGenericMarker<>), typeof(GenericMarker<>));
        services.RequireSingletonService<IGenericMarker<Payload>>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().ThrowAsync<InvalidServiceLifetimeException>();
    }

    [Fact]
    public async Task should_prefer_the_closed_registration_over_the_open_generic_one()
    {
        // given — the container answers a closed request from a closed registration before any open generic
        var services = new ServiceCollection();
        services.AddSingleton<IGenericMarker<Payload>, GenericMarker<Payload>>();
        services.AddScoped(typeof(IGenericMarker<>), typeof(GenericMarker<>));
        services.RequireSingletonService<IGenericMarker<Payload>>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_report_a_missing_service_through_the_required_service_check()
    {
        // given
        var services = new ServiceCollection();
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception.MissingServices.Should().ContainSingle().Which.ServiceType.Should().Be<IMarker>();
    }

    [Fact]
    public async Task should_ignore_keyed_registrations()
    {
        // given — the store injects the unkeyed service, so a keyed scoped registration is not what it captures
        var services = new ServiceCollection();
        services.AddSingleton<IMarker, Marker>();
        services.AddKeyedScoped<IMarker, Marker>("tenant");
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_report_every_violation_in_a_single_exception_and_collapse_duplicates()
    {
        // given
        var services = new ServiceCollection();
        services.AddScoped<IMarker, Marker>();
        services.AddTransient<IOtherMarker, OtherMarker>();
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);
        services.RequireSingletonService<IOtherMarker>("the other store", "Register IOtherMarker as a singleton.");

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<InvalidServiceLifetimeException>()).Which;
        exception.Violations.Should().HaveCount(2);
        exception.Message.Should().Contain(_RequiredBy).And.Contain("the other store");
    }

    [Fact]
    public async Task should_not_resolve_the_service_while_checking_it()
    {
        // given — resolving would construct the service under test, so a factory that throws must never run
        var services = new ServiceCollection();
        services.AddSingleton<IMarker>(_ => throw new InvalidOperationException("resolved"));
        services.RequireSingletonService<IMarker>(_RequiredBy, _Remedy);

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void should_reject_blank_arguments()
    {
        var services = new ServiceCollection();

        FluentActions
            .Invoking(() => services.RequireSingletonService(null!, _RequiredBy, _Remedy))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => services.RequireSingletonService<IMarker>(" ", _Remedy))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() => services.RequireSingletonService<IMarker>(_RequiredBy, ""))
            .Should()
            .Throw<ArgumentException>();
    }

    private static async Task _RunStartingAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();

        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(AbortToken);
        }
    }

    private interface IMarker;

    private sealed class Marker : IMarker;

    private interface IOtherMarker;

    private sealed class OtherMarker : IOtherMarker;

    private interface IGenericMarker<T>;

    private sealed class GenericMarker<T> : IGenericMarker<T>;

    private sealed record Payload;
}
