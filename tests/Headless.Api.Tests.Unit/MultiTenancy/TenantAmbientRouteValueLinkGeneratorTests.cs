// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the decorator contract in isolation: the ambient tenant route value is promoted into a
/// COPY of the explicit values only when the explicit values lack it, an explicit value (even
/// <c>null</c>) always wins, the request's route values back up a <c>null</c> ambient dictionary
/// (the <c>GetPathByName</c> surface), the context-free overloads pass through unchanged,
/// and <see cref="RouteTenantIdentifierSourceOptions.PromoteAmbientRouteValue"/> is honored at
/// call time. Also pins the once-only registration through <c>AddRouteSource</c>.
/// </summary>
public sealed class TenantAmbientRouteValueLinkGeneratorTests : TestBase
{
    [Fact]
    public void should_promote_the_ambient_tenant_when_the_explicit_values_lack_it()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        var values = new RouteValueDictionary { ["action"] = "Other" };
        var ambientValues = new RouteValueDictionary { ["tenant"] = "acme", ["action"] = "Index" };

        generator.GetPathByAddress(context, "address", values, ambientValues);

        inner.Values.Should().NotBeSameAs(values);
        inner.Values.Should().BeEquivalentTo(new RouteValueDictionary { ["action"] = "Other", ["tenant"] = "acme" });
        inner.AmbientValues.Should().BeSameAs(ambientValues);
        inner.HttpContext.Should().BeSameAs(context);
        inner.Address.Should().Be("address");
    }

    [Fact]
    public void should_promote_the_ambient_tenant_on_the_uri_overload()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        var values = new RouteValueDictionary { ["action"] = "Other" };
        var ambientValues = new RouteValueDictionary { ["tenant"] = "acme" };

        generator.GetUriByAddress(context, "address", values, ambientValues, scheme: "https");

        inner.Values.Should().NotBeSameAs(values);
        inner.Values!["tenant"].Should().Be("acme");
        inner.Values["action"].Should().Be("Other");
        inner.AmbientValues.Should().BeSameAs(ambientValues);
        inner.Scheme.Should().Be("https");
    }

    [Fact]
    public void should_pass_an_explicit_tenant_through_untouched()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        var values = new RouteValueDictionary { ["tenant"] = "globex" };
        var ambientValues = new RouteValueDictionary { ["tenant"] = "acme" };

        generator.GetPathByAddress(context, "address", values, ambientValues);

        inner.Values.Should().BeSameAs(values);
        inner.Values!["tenant"].Should().Be("globex");
        inner.AmbientValues.Should().BeSameAs(ambientValues);
    }

    [Fact]
    public void should_treat_an_explicit_null_tenant_as_explicit()
    {
        // A caller that clears the value on purpose (new { tenant = (string?)null }) has made an
        // explicit choice; promotion would silently override it.
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        var values = new RouteValueDictionary { ["tenant"] = null };
        var ambientValues = new RouteValueDictionary { ["tenant"] = "acme" };

        generator.GetPathByAddress(context, "address", values, ambientValues);

        inner.Values.Should().BeSameAs(values);
        inner.Values!["tenant"].Should().BeNull();
    }

    [Fact]
    public void should_promote_from_the_request_route_values_when_ambient_values_are_null()
    {
        // GetPathByName passes ambientValues: null, so the request's own route values are the only
        // place the tenant segment can be recovered from.
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "acme";
        var values = new RouteValueDictionary { ["id"] = 7 };

        generator.GetPathByAddress(context, "address", values, ambientValues: null);

        inner.Values.Should().NotBeSameAs(values);
        inner.Values!["tenant"].Should().Be("acme");
        inner.Values["id"].Should().Be(7);
        inner.AmbientValues.Should().BeNull();
    }

    [Fact]
    public void should_prefer_the_ambient_value_over_the_request_route_value()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "from-request";
        var ambientValues = new RouteValueDictionary { ["tenant"] = "from-ambient" };

        generator.GetPathByAddress(context, "address", new RouteValueDictionary(), ambientValues);

        inner.Values!["tenant"].Should().Be("from-ambient");
    }

    [Fact]
    public void should_pass_values_through_when_no_tenant_is_available()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        var values = new RouteValueDictionary { ["action"] = "Other" };
        var ambientValues = new RouteValueDictionary { ["action"] = "Index" };

        generator.GetPathByAddress(context, "address", values, ambientValues);

        inner.Values.Should().BeSameAs(values);
        inner.Values.Should().NotContainKey("tenant");
    }

    [Fact]
    public void should_promote_only_the_configured_route_value_name()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner, routeValueName: "slug");
        var context = new DefaultHttpContext();
        var values = new RouteValueDictionary();
        var ambientValues = new RouteValueDictionary
        {
            ["slug"] = "acme",
            ["tenant"] = "ignored",
            ["id"] = 7,
        };

        generator.GetPathByAddress(context, "address", values, ambientValues);

        inner.Values.Should().BeEquivalentTo(new RouteValueDictionary { ["slug"] = "acme" });
    }

    [Fact]
    public void should_not_mutate_the_caller_dictionaries()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "acme";
        var values = new RouteValueDictionary { ["action"] = "Other" };
        var ambientValues = new RouteValueDictionary { ["tenant"] = "acme", ["action"] = "Index" };

        generator.GetPathByAddress(context, "address", values, ambientValues);
        generator.GetPathByAddress(context, "address", values, ambientValues: null);

        values.Should().BeEquivalentTo(new RouteValueDictionary { ["action"] = "Other" });
        ambientValues.Should().BeEquivalentTo(new RouteValueDictionary { ["tenant"] = "acme", ["action"] = "Index" });
        context.Request.RouteValues.Should().BeEquivalentTo(new RouteValueDictionary { ["tenant"] = "acme" });
    }

    [Fact]
    public void should_pass_values_through_on_the_context_free_overloads()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner);
        var values = new RouteValueDictionary { ["action"] = "Other" };

        generator.GetPathByAddress("address", values, pathBase: "/base");
        inner.Values.Should().BeSameAs(values);
        inner.HttpContext.Should().BeNull();
        inner.PathBase.Should().Be(new PathString("/base"));

        generator.GetUriByAddress("address", values, "https", new HostString("example.com"));
        inner.Values.Should().BeSameAs(values);
        inner.HttpContext.Should().BeNull();
        inner.Scheme.Should().Be("https");
        inner.Host.Should().Be(new HostString("example.com"));
    }

    [Fact]
    public void should_not_promote_when_promotion_is_disabled()
    {
        var inner = new RecordingLinkGenerator();
        var generator = _CreateGenerator(inner, promote: false);
        var context = new DefaultHttpContext();
        context.Request.RouteValues["tenant"] = "acme";
        var values = new RouteValueDictionary { ["action"] = "Other" };
        var ambientValues = new RouteValueDictionary { ["tenant"] = "acme" };

        generator.GetPathByAddress(context, "address", values, ambientValues);

        inner.Values.Should().BeSameAs(values);
        inner.Values.Should().NotContainKey("tenant");
        inner.AmbientValues.Should().BeSameAs(ambientValues);
    }

    [Fact]
    public void should_forward_the_inner_result_and_the_remaining_arguments()
    {
        var inner = new RecordingLinkGenerator { Result = "/generated" };
        var generator = _CreateGenerator(inner);
        var context = new DefaultHttpContext();
        var options = new LinkOptions { LowercaseUrls = true };

        var result = generator.GetPathByAddress(
            context,
            "address",
            new RouteValueDictionary(),
            ambientValues: null,
            pathBase: "/base",
            fragment: new FragmentString("#top"),
            options: options
        );

        result.Should().Be("/generated");
        inner.PathBase.Should().Be(new PathString("/base"));
        inner.Fragment.Should().Be(new FragmentString("#top"));
        inner.Options.Should().BeSameAs(options);
    }

    [Fact]
    public void should_reject_null_constructor_arguments()
    {
        var innerAct = () =>
            new TenantAmbientRouteValueLinkGenerator(null!, Options.Create(new RouteTenantIdentifierSourceOptions()));
        var optionsAct = () => new TenantAmbientRouteValueLinkGenerator(new RecordingLinkGenerator(), null!);

        innerAct.Should().Throw<ArgumentNullException>();
        optionsAct.Should().Throw<ArgumentNullException>();
    }

    // --- registration: wrap exactly once, regardless of AddRouting()/AddControllers() order ---

    [Fact]
    public void should_wrap_the_link_generator_once_for_repeat_add_route_source_calls()
    {
        var services = new ServiceCollection().AddLogging();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource().AddRouteSource("slug").AddRouteSource(_ => { });

        services.Where(d => d.ServiceType == typeof(LinkGenerator)).Should().ContainSingle();

        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<LinkGenerator>();

        var decorator = generator.Should().BeOfType<TenantAmbientRouteValueLinkGenerator>().Subject;
        decorator.Inner.Should().NotBeOfType<TenantAmbientRouteValueLinkGenerator>();
    }

    [Fact]
    public void should_wrap_the_link_generator_when_routing_was_registered_first()
    {
        var services = new ServiceCollection().AddLogging().AddRouting();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource();
        // A later AddRouting() (what AddControllers() does) is TryAdd-based and must not undo the wrap.
        services.AddRouting();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<LinkGenerator>().Should().BeOfType<TenantAmbientRouteValueLinkGenerator>();
    }

    [Fact]
    public void should_wrap_the_link_generator_for_the_configuration_overload()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddRouteSource(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<LinkGenerator>().Should().BeOfType<TenantAmbientRouteValueLinkGenerator>();
    }

    [Fact]
    public void should_default_promotion_to_enabled()
    {
        new RouteTenantIdentifierSourceOptions().PromoteAmbientRouteValue.Should().BeTrue();
    }

    private static TenantAmbientRouteValueLinkGenerator _CreateGenerator(
        LinkGenerator inner,
        string? routeValueName = null,
        bool promote = true
    )
    {
        var options = new RouteTenantIdentifierSourceOptions { PromoteAmbientRouteValue = promote };

        if (routeValueName is not null)
        {
            options.RouteValueName = routeValueName;
        }

        return new TenantAmbientRouteValueLinkGenerator(inner, Options.Create(options));
    }

    /// <summary>Fake inner generator that records the arguments of the last call.</summary>
    private sealed class RecordingLinkGenerator : LinkGenerator
    {
        public string? Result { get; init; }

        public HttpContext? HttpContext { get; private set; }

        public object? Address { get; private set; }

        public RouteValueDictionary? Values { get; private set; }

        public RouteValueDictionary? AmbientValues { get; private set; }

        public PathString? PathBase { get; private set; }

        public FragmentString Fragment { get; private set; }

        public LinkOptions? Options { get; private set; }

        public string? Scheme { get; private set; }

        public HostString? Host { get; private set; }

        public override string? GetPathByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary? ambientValues = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions? options = null
        )
        {
            _Record(httpContext, address, values, ambientValues, pathBase, fragment, options, scheme: null, host: null);
            return Result;
        }

        public override string? GetPathByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions? options = null
        )
        {
            _Record(null, address, values, null, pathBase, fragment, options, scheme: null, host: null);
            return Result;
        }

        public override string? GetUriByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary? ambientValues = null,
            string? scheme = null,
            HostString? host = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions? options = null
        )
        {
            _Record(httpContext, address, values, ambientValues, pathBase, fragment, options, scheme, host);
            return Result;
        }

        public override string? GetUriByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            string scheme,
            HostString host,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions? options = null
        )
        {
            _Record(null, address, values, null, pathBase, fragment, options, scheme, host);
            return Result;
        }

        private void _Record(
            HttpContext? httpContext,
            object? address,
            RouteValueDictionary values,
            RouteValueDictionary? ambientValues,
            PathString? pathBase,
            FragmentString fragment,
            LinkOptions? options,
            string? scheme,
            HostString? host
        )
        {
            HttpContext = httpContext;
            Address = address;
            Values = values;
            AmbientValues = ambientValues;
            PathBase = pathBase;
            Fragment = fragment;
            Options = options;
            Scheme = scheme;
            Host = host;
        }
    }
}
