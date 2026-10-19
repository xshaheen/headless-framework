// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.MultiTenancy;

/// <summary>
/// Pins the description <c>ResolveFromCatalog()</c> registers: it reports the configured sources from registrations,
/// so API documentation never reads the internal source types.
/// </summary>
public sealed class TenantCatalogResolutionDescriptionTests : TestBase
{
    [Fact]
    public void should_describe_the_header_names_host_templates_and_non_header_source()
    {
        var description = _Describe(sources =>
            sources.AddHeaderSource("X-Tenant").AddHeaderSource("x-tenant").AddHostSource("{tenant}.example.com")
        );

        description.IsConfigured.Should().BeTrue();
        description.DetailedResolutionErrors.Should().BeFalse();
        description.HeaderNames.Should().Equal("X-Tenant");
        description.HostTemplates.Should().Equal("{tenant}.example.com");
        description.HasNonHeaderSource.Should().BeTrue();
    }

    [Fact]
    public void should_report_no_non_header_source_when_only_the_header_source_is_registered()
    {
        var description = _Describe(sources => sources.AddHeaderSource(), detailedErrors: true);

        description.IsConfigured.Should().BeTrue();
        description.DetailedResolutionErrors.Should().BeTrue();
        description.HeaderNames.Should().Equal(HeaderTenantIdentifierSourceOptions.DefaultHeaderName);
        description.HostTemplates.Should().BeEmpty();
        description.HasNonHeaderSource.Should().BeFalse();
    }

    [Fact]
    public void should_report_not_configured_when_no_identifier_source_is_registered()
    {
        var description = _Describe(_ => { }, detailedErrors: true);

        description.IsConfigured.Should().BeFalse();
        description.DetailedResolutionErrors.Should().BeFalse();
        description.HeaderNames.Should().BeEmpty();
        description.HostTemplates.Should().BeEmpty();
        description.HasNonHeaderSource.Should().BeFalse();
    }

    [Fact]
    public void should_reject_null_source_lists()
    {
        var headers = () => new TenantCatalogResolutionDescription(true, false, null!, [], false);
        var hosts = () => new TenantCatalogResolutionDescription(true, false, [], null!, false);

        headers.Should().Throw<ArgumentNullException>();
        hosts.Should().Throw<ArgumentNullException>();
    }

    private static TenantCatalogResolutionDescription _Describe(
        Action<HeadlessTenantCatalogResolutionBuilder> configureSources,
        bool detailedErrors = false
    )
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromCatalog(configureSources)));
        builder.Services.Configure<TenantCatalogOptions>(options => options.DetailedResolutionErrors = detailedErrors);

        using var services = builder.Services.BuildServiceProvider();

        return services.GetRequiredService<TenantCatalogResolutionDescription>();
    }
}
