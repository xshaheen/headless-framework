// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the R3 header source behavior in isolation: exactly one value across the configured names is
/// yielded raw, an absent or whitespace-only value is <see cref="TenantIdentifierSourceResultKind.None"/>,
/// any second value (repeated lines or two configured names both present) is
/// <see cref="TenantIdentifierSourceResultKind.Invalid"/>, and every consult appends the configured
/// names to the response <c>Vary</c> header regardless of outcome (KTD4). Also pins the KTD3 builder
/// registration semantics for <c>AddHeaderSource</c>.
/// </summary>
public sealed class HeaderTenantIdentifierSourceTests : TestBase
{
    private const string LegacyHeader = "X-Legacy-Tenant";

    [Fact]
    public void should_find_the_identifier_from_a_single_default_header_and_append_vary()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderTenantIdentifierSourceOptions.DefaultHeaderName] = "acme";

        var result = source.GetIdentifier(context);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("acme");
        _VaryEntries(context).Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
    }

    [Fact]
    public void should_return_none_and_still_append_vary_when_the_header_is_absent()
    {
        // KTD4: a response resolved by a later source (or none) and cached without Vary would be served
        // to a subsequent request carrying a different header value — so Vary is stamped even on None.
        var source = _CreateSource();
        var context = new DefaultHttpContext();

        var result = source.GetIdentifier(context);

        result.Should().Be(TenantIdentifierSourceResult.None);
        _VaryEntries(context).Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
    }

    [Fact]
    public void should_return_none_for_a_whitespace_only_header_value()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderTenantIdentifierSourceOptions.DefaultHeaderName] = "   ";

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.None);
    }

    [Fact]
    public void should_return_invalid_and_still_append_vary_when_the_header_is_repeated()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderTenantIdentifierSourceOptions.DefaultHeaderName] = new StringValues([
            "acme",
            "globex",
        ]);

        var result = source.GetIdentifier(context);

        result.Should().Be(TenantIdentifierSourceResult.Invalid);
        _VaryEntries(context).Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
    }

    [Fact]
    public void should_return_invalid_when_the_header_is_repeated_with_the_same_value()
    {
        // Ambiguity is any second value, not a disagreement: two equal lines are still rejected so the
        // rule needs no value comparison (KTD4).
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderTenantIdentifierSourceOptions.DefaultHeaderName] = new StringValues([
            "acme",
            "acme",
        ]);

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.Invalid);
    }

    [Fact]
    public void should_return_invalid_when_two_configured_names_are_both_present()
    {
        var source = _CreateSource(HeaderTenantIdentifierSourceOptions.DefaultHeaderName, LegacyHeader);
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderTenantIdentifierSourceOptions.DefaultHeaderName] = "a";
        context.Request.Headers[LegacyHeader] = "b";

        source.GetIdentifier(context).Should().Be(TenantIdentifierSourceResult.Invalid);
    }

    [Fact]
    public void should_find_the_identifier_from_the_second_configured_name_when_only_it_is_present()
    {
        var source = _CreateSource(HeaderTenantIdentifierSourceOptions.DefaultHeaderName, LegacyHeader);
        var context = new DefaultHttpContext();
        context.Request.Headers[LegacyHeader] = "acme";

        var result = source.GetIdentifier(context);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("acme");
    }

    [Fact]
    public void should_pass_a_single_comma_separated_line_through_as_one_value()
    {
        // One header line is one value: the source never splits on commas, so "a,b" reaches the catalog
        // unchanged and can only ever match a tenant whose identifier literally contains a comma (KTD4).
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderTenantIdentifierSourceOptions.DefaultHeaderName] = "a,b";

        var result = source.GetIdentifier(context);

        result.Kind.Should().Be(TenantIdentifierSourceResultKind.Found);
        result.Identifier.Should().Be("a,b");
    }

    [Fact]
    public void should_append_to_an_existing_vary_header_exactly_once_across_repeat_consults()
    {
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Response.Headers.Vary = "Accept";

        source.GetIdentifier(context);
        source.GetIdentifier(context);

        _VaryEntries(context).Should().Equal("Accept", HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
    }

    [Fact]
    public void should_not_duplicate_a_vary_entry_that_differs_only_by_case()
    {
        // Header names are case-insensitive; a consumer that already stamped "x-tenant" must not get a
        // second entry.
        var source = _CreateSource();
        var context = new DefaultHttpContext();
        context.Response.Headers.Vary = "Accept, x-tenant";

        source.GetIdentifier(context);

        _VaryEntries(context).Should().Equal("Accept", "x-tenant");
    }

    [Fact]
    public void should_append_every_configured_name_to_vary()
    {
        var source = _CreateSource(HeaderTenantIdentifierSourceOptions.DefaultHeaderName, LegacyHeader);
        var context = new DefaultHttpContext();

        source.GetIdentifier(context);

        _VaryEntries(context).Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName, LegacyHeader);
    }

    [Fact]
    public void should_reject_a_null_context()
    {
        var source = _CreateSource();

        var act = () => source.GetIdentifier(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- registration (KTD3: descriptor dedupes, options contributions accumulate) ---

    [Fact]
    public void should_register_one_header_source_descriptor_for_repeat_add_header_source_calls()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource().AddHeaderSource();

        var descriptors = services.Where(d => d.ServiceType == typeof(ITenantIdentifierSource)).ToList();

        descriptors.Should().HaveCount(1);
        descriptors[0].ImplementationType.Should().Be(typeof(HeaderTenantIdentifierSource));
    }

    [Fact]
    public void should_default_the_header_names_to_x_tenant()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource();

        _ResolveOptions(services).HeaderNames.Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
    }

    [Fact]
    public void should_replace_the_untouched_default_with_the_string_overload_name()
    {
        // The string overload must not silently keep the default beside the requested name: an operator
        // who asked for X-Legacy alone would otherwise let any client select a tenant via X-Tenant, and
        // a client sending both would be rejected as ambiguous.
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource("X-Legacy");

        _ResolveOptions(services).HeaderNames.Should().Equal("X-Legacy");
    }

    [Fact]
    public void should_accumulate_string_overload_names_after_the_first_and_keep_one_descriptor()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource(HeaderTenantIdentifierSourceOptions.DefaultHeaderName).AddHeaderSource(LegacyHeader);

        services.Where(d => d.ServiceType == typeof(ITenantIdentifierSource)).Should().HaveCount(1);
        _ResolveOptions(services)
            .HeaderNames.Should()
            .Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName, LegacyHeader);
    }

    [Fact]
    public void should_append_the_string_overload_name_after_a_configure_contribution()
    {
        // A configure callback that mutates the default list makes it "touched" — later string
        // contributions then append rather than replace (KTD3).
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource(options => options.HeaderNames.Add(LegacyHeader)).AddHeaderSource("X-Other");

        _ResolveOptions(services)
            .HeaderNames.Should()
            .Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName, LegacyHeader, "X-Other");
    }

    [Fact]
    public void should_apply_the_configure_overload()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource(options => options.HeaderNames = ["X-Custom"]);

        _ResolveOptions(services).HeaderNames.Should().Equal("X-Custom");
    }

    [Fact]
    public void should_replace_the_untouched_default_when_configuration_lists_header_names()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["HeaderNames:0"] = "X-Custom" })
            .Build();
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource(configuration);

        _ResolveOptions(services).HeaderNames.Should().Equal("X-Custom");
    }

    [Fact]
    public void should_keep_the_default_when_configuration_has_no_header_names()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        builder.AddHeaderSource(configuration);

        _ResolveOptions(services).HeaderNames.Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
    }

    [Fact]
    public void should_reject_null_add_header_source_arguments()
    {
        var services = new ServiceCollection();
        var builder = new HeadlessTenantCatalogResolutionBuilder(services);

        var nameAct = () => builder.AddHeaderSource((string)null!);
        var whitespaceAct = () => builder.AddHeaderSource("   ");
        var configureAct = () => builder.AddHeaderSource((Action<HeaderTenantIdentifierSourceOptions>)null!);
        var configurationAct = () => builder.AddHeaderSource((IConfiguration)null!);

        nameAct.Should().Throw<ArgumentException>();
        whitespaceAct.Should().Throw<ArgumentException>();
        configureAct.Should().Throw<ArgumentNullException>();
        configurationAct.Should().Throw<ArgumentNullException>();
    }

    // --- helpers ---

    private static HeaderTenantIdentifierSource _CreateSource(params string[] headerNames)
    {
        var options = new HeaderTenantIdentifierSourceOptions();

        if (headerNames.Length > 0)
        {
            options.HeaderNames = [.. headerNames];
        }

        return new HeaderTenantIdentifierSource(Options.Create(options));
    }

    private static HeaderTenantIdentifierSourceOptions _ResolveOptions(IServiceCollection services)
    {
        return services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HeaderTenantIdentifierSourceOptions>>()
            .Value;
    }

    /// <summary>
    /// Flattens the response <c>Vary</c> header into its tokens whether the source stamped separate
    /// entries or a consumer pre-stamped a comma-joined line, so assertions are shape-independent.
    /// </summary>
    private static List<string> _VaryEntries(HttpContext context)
    {
        return context
            .Response.Headers.Vary.SelectMany(entry => (entry ?? string.Empty).Split(','))
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToList();
    }
}
