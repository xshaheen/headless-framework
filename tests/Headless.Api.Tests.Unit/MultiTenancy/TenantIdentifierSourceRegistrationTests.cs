// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the registration semantics: type registrations deduplicate keep-first through
/// <c>TryAddEnumerable</c>, while instance and delegate registrations always append, and the container
/// materializes <c>IEnumerable&lt;ITenantIdentifierSource&gt;</c> in that same insertion order.
/// </summary>
public sealed class TenantIdentifierSourceRegistrationTests : TestBase
{
    [Fact]
    public void should_keep_the_first_position_when_the_same_type_registers_twice()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddSource<FirstStubSource>().AddSource<SecondStubSource>().AddSource<FirstStubSource>();

        var descriptors = services.Where(d => d.ServiceType == typeof(ITenantIdentifierSource)).ToList();

        descriptors.Should().HaveCount(2);
        descriptors[0].ImplementationType.Should().Be<FirstStubSource>();
        descriptors[1].ImplementationType.Should().Be<SecondStubSource>();
    }

    [Fact]
    public void should_keep_both_instance_registrations_of_one_type()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddSource(new RecordingStubSource("first")).AddSource(new RecordingStubSource("second"));

        services.Where(d => d.ServiceType == typeof(ITenantIdentifierSource)).Should().HaveCount(2);
    }

    [Fact]
    public void should_resolve_a_delegate_after_an_earlier_type_entry()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddSource<FirstStubSource>().AddSource(_ => "acme");

        var sources = services
            .BuildServiceProvider()
            .GetRequiredService<IEnumerable<ITenantIdentifierSource>>()
            .ToList();

        sources.Should().HaveCount(2);
        sources[0].Should().BeOfType<FirstStubSource>();
        sources[1].GetIdentifier(new DefaultHttpContext()).Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_map_a_null_delegate_return_to_none()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddSource(_ => null);

        var source = services
            .BuildServiceProvider()
            .GetRequiredService<IEnumerable<ITenantIdentifierSource>>()
            .Single();

        source.GetIdentifier(new DefaultHttpContext()).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_map_a_blank_delegate_return_to_none()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddSource(_ => "   ");

        var source = services
            .BuildServiceProvider()
            .GetRequiredService<IEnumerable<ITenantIdentifierSource>>()
            .Single();

        source.GetIdentifier(new DefaultHttpContext()).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_propagate_a_throwing_delegate_unchanged()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddSource(_ => throw new InvalidOperationException("Simulated source fault."));

        var source = services
            .BuildServiceProvider()
            .GetRequiredService<IEnumerable<ITenantIdentifierSource>>()
            .Single();

        var act = () => source.GetIdentifier(new DefaultHttpContext());

        act.Should().ThrowExactly<InvalidOperationException>().WithMessage("Simulated source fault.");
    }

    [Fact]
    public void should_reject_a_null_instance_and_a_null_delegate()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        var instanceAct = () => builder.AddSource((ITenantIdentifierSource)null!);
        var delegateAct = () => builder.AddSource((Func<HttpContext, string?>)null!);

        instanceAct.Should().Throw<ArgumentNullException>();
        delegateAct.Should().Throw<ArgumentNullException>();
    }

    private sealed class FirstStubSource : ITenantIdentifierSource
    {
        public TenantIdentifierSourceResult GetIdentifier(HttpContext context) => TenantIdentifierSourceResult.None;
    }

    private sealed class SecondStubSource : ITenantIdentifierSource
    {
        public TenantIdentifierSourceResult GetIdentifier(HttpContext context) => TenantIdentifierSourceResult.None;
    }

    private sealed class RecordingStubSource(string label) : ITenantIdentifierSource
    {
        public TenantIdentifierSourceResult GetIdentifier(HttpContext context) =>
            TenantIdentifierSourceResult.Found(label);
    }
}
