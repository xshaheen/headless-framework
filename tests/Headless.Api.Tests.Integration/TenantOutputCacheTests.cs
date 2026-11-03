// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// ASP.NET Core output caching keys an entry by host, path, and the policy's vary-by rules, never by the response
/// <c>Vary</c> header. A tenant selected by a request header is in none of those, so catalog resolution has to put the
/// resolved tenant into the key itself.
/// </summary>
public sealed class TenantOutputCacheTests : TestBase
{
    private const string TenantHeader = HeaderTenantIdentifierSourceOptions.DefaultHeaderName;

    [Fact]
    public async Task should_not_serve_one_tenants_cached_response_to_another_tenant_selected_by_header()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var acme = await _GetAsync(client, "acme");
        var acmeAgain = await _GetAsync(client, "acme");
        var globex = await _GetAsync(client, "globex");
        var host = await _GetAsync(client, tenant: null);

        acmeAgain.Should().Be(acme, "the same tenant's repeat request is served from the output cache");
        acme.Should().StartWith("ten_acme:");
        globex.Should().StartWith("ten_globex:", "another tenant must never receive acme's cached body");
        host.Should().StartWith("host:", "a request without a tenant must never receive a tenant's cached body");
    }

    private async Task<WebApplication> _CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });

        builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());
        builder.Services.AddOutputCache();

        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(o =>
                {
                    o.Tenants.Add(new TenantInfo("ten_acme", "acme", "Acme Inc", isEnabled: true));
                    o.Tenants.Add(new TenantInfo("ten_globex", "globex", "Globex", isEnabled: true));
                })
            );

            tenancy.Http(http => http.ResolveFromCatalog(sources => sources.AddHeaderSource()));
        });

        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseStatusCodesRewriter();
        app.UseRouting();
        app.UseHeadlessTenantCatalogResolution();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseOutputCache();

        // The per-invocation id shows whether the body came from the cache or a fresh run of the handler.
        app.MapGet(
                "/branding",
                (ICurrentTenant currentTenant) =>
                    Results.Text($"{currentTenant.Id ?? "host"}:{Guid.NewGuid():N}", "text/plain")
            )
            .CacheOutput();

        await app.StartAsync(AbortToken);
        return app;
    }

    private static async Task<string> _GetAsync(HttpClient client, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/branding");

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await client.SendAsync(request, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync(AbortToken);
    }
}
