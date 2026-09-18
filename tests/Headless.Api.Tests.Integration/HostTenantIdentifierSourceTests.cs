// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Caching;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.MultiTenancy.Resources;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Pins the host source end to end through the real catalog pipeline: port-excluded host matching,
/// ignored identifiers ending resolution with no store call, the fail-closed unknown
/// rejection, the whole-host (custom-domain) override, reading <c>Host</c> and never
/// <c>X-Forwarded-Host</c> directly, and startup failure on an unparsable template.
/// </summary>
public sealed class HostTenantIdentifierSourceTests : TestBase
{
    [Fact]
    public async Task should_resolve_the_tenant_from_a_case_and_port_mismatched_subdomain()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // Port excluded, matching case-insensitive, capture keeps casing, catalog normalizes.
        var tenant = await _GetTenantAsync(client, "ACME.example.com:8443");

        tenant.Id.Should().Be("ten_123");
        tenant.Name.Should().Be("Acme Inc");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_end_resolution_as_host_context_without_a_store_call_for_an_ignored_identifier()
    {
        var store = new CountingTenantStore();
        await using var app = await _CreateAppAsync(store: store, ignoredIdentifiers: ["www"]);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // www is an ignored identifier on the CATALOG options (there is no source-level list), so
        // resolution ends as host context; the seeded store must never be consulted.
        var tenant = await _GetTenantAsync(client, "www.example.com");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
        store.IdentifierLookups.Should().Be(0);
    }

    [Fact]
    public async Task should_return_the_fail_closed_404_with_no_store_for_an_unknown_subdomain()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, "ghost.example.com");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.ResolutionFailed);
    }

    [Fact]
    public async Task should_resolve_a_whole_host_identifier_under_the_catalog_pattern_override()
    {
        // The whole-host (custom-domain) form ships only through the catalog-wide override —
        // a bare {tenant} template, MaxIdentifierLength raised, and IdentifierPattern replaced with a
        // hostname-shaped regex defined locally in this test (a shipped shared pattern is
        // deliberately out of scope).
        await using var app = await _CreateAppAsync(
            template: "{tenant}",
            configureCatalog: options =>
            {
                options.MaxIdentifierLength = 253;
                options.IdentifierPattern = new Regex(
                    "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$",
                    RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                    RegexPatterns.MatchTimeout
                );
            }
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        var tenant = await _GetTenantAsync(client, "orders.acme.com");

        tenant.Id.Should().Be("ten_789");
    }

    [Fact]
    public async Task should_read_the_host_header_not_the_forwarded_host()
    {
        await using var app = await _CreateAppAsync();
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // Forwarded headers are NOT enabled on this host, so X-Forwarded-Host must be ignored —
        // the source reads Request.Host, which still carries the loopback host. It matches no
        // template, so the request proceeds as host context rather than resolving acme.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Host = "app.internal";
        request.Headers.Add("X-Forwarded-Host", "acme.example.com");
        using var response = await client.SendAsync(request, AbortToken);

        response.EnsureSuccessStatusCode();
        var tenant = await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken);

        tenant!.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_host_startup_when_a_template_is_unparsable()
    {
        // Two {tenant} tokens — the validator's message (naming the template and the rule)
        // must surface through ValidateOnStart at Build/StartAsync.
        var act = () => _CreateAppAsync(template: "{tenant}.{tenant}.com");

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*{tenant}.{tenant}.com*")
            .WithMessage("*exactly one*");
    }

    // --- app factory and helpers ---

    private async Task<WebApplication> _CreateAppAsync(
        string template = "{tenant}.example.com",
        Action<TenantCatalogOptions>? configureCatalog = null,
        CountingTenantStore? store = null,
        IList<string>? ignoredIdentifiers = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        HttpTenancyTestHarness.AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

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

        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.Catalog(catalog =>
                catalog.UseInMemory(o =>
                {
                    o.Tenants.Add(new TenantInfo("ten_123", "acme", "Acme Inc", isEnabled: true));
                    o.Tenants.Add(new TenantInfo("ten_789", "orders.acme.com", "Acme Orders", isEnabled: true));
                })
            );

            tenancy.Http(http =>
                http.ResolveFromCatalog(sources =>
                {
                    if (template == "{tenant}")
                    {
                        sources.AddHostSource(options => options.Templates.Add("{tenant}"));
                    }
                    else
                    {
                        sources.AddHostSource(template);
                    }
                })
            );
        });

        if (ignoredIdentifiers is not null)
        {
            builder.Services.PostConfigure<TenantCatalogOptions>(o => o.IgnoredIdentifiers = ignoredIdentifiers);
        }

        if (configureCatalog is not null)
        {
            builder.Services.PostConfigure(configureCatalog);
        }

        if (store is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton<ITenantStore>(store));
        }

        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseStatusCodesRewriter();
        app.UseRouting();
        app.UseHeadlessTenantCatalogResolution();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet(
            "/tenant",
            (ICurrentTenant currentTenant) =>
                Results.Json(
                    new HostTenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable, currentTenant.Name)
                )
        );

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<HostTenantCatalogResponse> _GetTenantAsync(HttpClient client, string host)
    {
        using var response = await _SendAsync(client, host);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<HostTenantCatalogResponse>(cancellationToken: AbortToken))!;
    }

    private async Task<HttpResponseMessage> _SendAsync(HttpClient client, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Host = host;

        return await client.SendAsync(request, AbortToken);
    }

    /// <summary>
    /// Replaces the in-memory store so a test can prove resolution never reached the catalog: every
    /// identifier lookup is counted.
    /// </summary>
    private sealed class CountingTenantStore : ITenantStore
    {
        public int IdentifierLookups { get; private set; }

        public Task<TenantInfo?> FindByIdentifierAsync(
            string normalizedIdentifier,
            CancellationToken cancellationToken = default
        )
        {
            IdentifierLookups++;
            return Task.FromResult<TenantInfo?>(null);
        }

        public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TenantInfo?>(null);
        }
    }
}

internal sealed record HostTenantCatalogResponse(string? Id, bool IsAvailable, string? Name = null);
