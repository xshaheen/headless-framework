// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the R2 route source behavior in isolation: the string route value named by the option
/// (default <c>tenant</c>) is yielded raw and unchanged, and a missing, non-string, or
/// whitespace-only value yields <see cref="TenantIdentifierSourceResultKind.None"/>. Also pins the
/// KTD3 builder registration semantics for <c>AddRouteSource</c>.
/// </summary>
public sealed class RouteTenantIdentifierSourceTests : TestBase
{
    [Fact]
    public void should_find_the_identifier_from_the_default_route_value_name()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "acme";

        var result = source.GetIdentifier(context);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_return_none_when_the_route_value_is_missing()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_none_for_a_non_string_route_value()
    {
        // A route value of another type (a route default bound to int, a Guid constraint) is not an
        // identifier — the source must not ToString() it into one (R2).
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = 42;

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_honor_a_custom_route_value_name()
    {
        var source = _CreateSource("slug");
        var context = new DefaultHttpContext();
        context.Request.RouteValues["slug"] = "acme";

        var result = source.GetIdentifier(context);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_return_none_for_a_whitespace_route_value()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "   ";

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    // --- registration (KTD3: descriptor dedupes, options contributions accumulate) ---

    [Fact]
    public void should_register_one_route_source_descriptor_for_repeat_add_route_source_calls()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource().AddRouteSource();

        var descriptors = services.Where(d => d.ServiceType == typeof(ITenantIdentifierSource)).ToList();

        descriptors.Should().HaveCount(1);
        descriptors[0].ImplementationType.Should().Be(typeof(RouteTenantIdentifierSource));
    }

    [Fact]
    public void should_default_the_route_value_name_to_tenant()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RouteTenantIdentifierSourceOptions>>().Value;

        options.RouteValueName.Should().Be(RouteTenantIdentifierSourceOptions.DefaultRouteValueName);
    }

    [Fact]
    public void should_apply_the_last_route_value_name_contribution()
    {
        // Options contributions accumulate in call order even though the descriptor deduplicates
        // (KTD3) — for the single-valued name that means the last contribution wins.
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource("slug").AddRouteSource("org");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RouteTenantIdentifierSourceOptions>>().Value;

        options.RouteValueName.Should().Be("org");
    }

    [Fact]
    public void should_apply_the_configure_overload()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource(options => options.RouteValueName = "slug");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RouteTenantIdentifierSourceOptions>>().Value;

        options.RouteValueName.Should().Be("slug");
    }

    [Fact]
    public void should_bind_the_route_value_name_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RouteValueName"] = "slug" })
            .Build();
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RouteTenantIdentifierSourceOptions>>().Value;

        options.RouteValueName.Should().Be("slug");
    }

    [Fact]
    public void should_reject_null_add_route_source_arguments()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        var nameAct = () => builder.AddRouteSource((string)null!);
        var whitespaceAct = () => builder.AddRouteSource("   ");
        var configureAct = () => builder.AddRouteSource((Action<RouteTenantIdentifierSourceOptions>)null!);
        var configurationAct = () => builder.AddRouteSource((IConfiguration)null!);

        nameAct.Should().Throw<ArgumentException>();
        whitespaceAct.Should().Throw<ArgumentException>();
        configureAct.Should().Throw<ArgumentNullException>();
        configurationAct.Should().Throw<ArgumentNullException>();
    }

    private static RouteTenantIdentifierSource _CreateSource(string? routeValueName = null)
    {
        var options = new RouteTenantIdentifierSourceOptions();

        if (routeValueName is not null)
        {
            options.RouteValueName = routeValueName;
        }

        return new RouteTenantIdentifierSource(Options.Create(options));
    }
}
