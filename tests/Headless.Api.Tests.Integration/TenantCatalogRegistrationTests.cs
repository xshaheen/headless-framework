// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Caching;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

public sealed class TenantCatalogRegistrationTests : TestBase
{
    private const string _IdentifierHeader = "X-Test-Catalog-Identifier";

    /// <summary>
    /// Guards the catalog registration path against a service descriptor the container cannot construct.
    /// Every other catalog and tenancy test opts out of service-provider validation, which is how an
    /// unbuildable <c>TenantCatalogResolutionMiddleware</c> singleton (a convention-based middleware whose
    /// first constructor parameter is <c>RequestDelegate</c>, never in the container) reached main and
    /// broke <c>builder.Build()</c> for every real host using <c>ResolveFromCatalog</c>.
    /// </summary>
    [Fact]
    public async Task should_build_a_catalog_host_when_service_provider_validation_is_left_at_its_default()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        HttpTenancyTestHarness.AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        // Validation is deliberately not configured here: ValidateServiceProviderOnStartup defaults to
        // true, which is what a real host gets, and it is the only reason Build() below exercises
        // ValidateOnBuild at all.
        builder.AddHeadless(configureServices: options =>
        {
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });

        builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());

        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(o => o.Tenants.Add(new TenantInfo("ten_123", "acme", "Acme Inc", isEnabled: true)))
            );

            tenancy.Http(http =>
                http.ResolveFromCatalog(catalogHttp =>
                    catalogHttp.AddSource(new HeaderTenantIdentifierSource(_IdentifierHeader))
                )
            );
        });

        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        // ValidateOnBuild runs here — a non-constructible descriptor surfaces as an aggregate exception.
        await using var app = builder.Build();

        app.Services.GetRequiredService<HeadlessServiceDefaultsOptions>()
            .Validation.ValidateServiceProviderOnStartup.Should()
            .BeTrue("the defect this test guards is only observable while ValidateOnBuild is enabled");
    }
}
