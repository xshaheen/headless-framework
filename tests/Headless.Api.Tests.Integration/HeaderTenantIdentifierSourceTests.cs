// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Api.ServiceDefaults;
using Headless.Caching;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.MultiTenancy.Resources;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Pins the header source end to end through the real catalog pipeline: a single header resolves
/// with <c>Vary</c> stamped, a repeated header line is rejected as invalid before any catalog call,
/// a custom name bound from configuration replaces the default, the claim-mismatch check still
/// applies to a header-selected tenant (and its documented no-claim limit holds), and a host source
/// registered first wins without the header source ever being consulted.
/// </summary>
public sealed class HeaderTenantIdentifierSourceTests : TestBase
{
    private const string TenantHeader = HeaderTenantIdentifierSourceOptions.DefaultHeaderName;
    private const string CustomTenantHeader = "X-Custom-Tenant";
    private const string LegacyTenantHeader = "X-Legacy-Tenant";

    [Fact]
    public async Task should_resolve_the_tenant_from_a_single_header_and_stamp_vary()
    {
        await using var app = await _CreateAppAsync(sources => sources.AddHeaderSource());
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, new Dictionary<string, string> { [TenantHeader] = "acme" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant = await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken);
        tenant!.Id.Should().Be("ten_123");
        tenant.Name.Should().Be("Acme Inc");
        response.Headers.Vary.Should().Contain(TenantHeader);
    }

    [Fact]
    public async Task should_stamp_vary_on_a_host_context_response_when_the_header_is_absent()
    {
        // The source is consulted (and finds nothing), so the cacheable success response must
        // still vary on the header — a cached copy would otherwise be served to a request that carries it.
        await using var app = await _CreateAppAsync(sources => sources.AddHeaderSource());
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(client, headers: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant = await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken);
        tenant!.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
        response.Headers.Vary.Should().Contain(TenantHeader);
    }

    [Fact]
    public async Task should_reject_a_repeated_header_line_before_any_catalog_call()
    {
        // Two X-Tenant lines. HttpClient folds repeated custom header values into one line, so the
        // request is written over a raw socket to prove Kestrel keeps them as two values.
        ConsultCountingStubSource.Consultations = 0;
        var store = new CountingTenantStore();
        await using var app = await _CreateAppAsync(
            sources => sources.AddHeaderSource().AddSource(new ConsultCountingStubSource()),
            store
        );

        var response = await _SendRawAsync(app, [(TenantHeader, "acme"), (TenantHeader, "globex")]);

        response.StatusCode.Should().Be(400);
        response.Headers.Should().Contain(h => h.Name == "Cache-Control" && h.Value == "no-store");
        _VaryTokens(response).Should().Contain(TenantHeader);
        using var doc = JsonDocument.Parse(response.Body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.IdentifierInvalid);

        store.IdentifierLookups.Should().Be(0);
        ConsultCountingStubSource.Consultations.Should().Be(0);
    }

    [Fact]
    public async Task should_reject_two_configured_names_both_present_before_any_catalog_call()
    {
        ConsultCountingStubSource.Consultations = 0;
        var store = new CountingTenantStore();
        await using var app = await _CreateAppAsync(
            sources =>
                sources
                    .AddHeaderSource(TenantHeader)
                    .AddHeaderSource(LegacyTenantHeader)
                    .AddSource(new ConsultCountingStubSource()),
            store
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(
            client,
            new Dictionary<string, string> { [TenantHeader] = "acme", [LegacyTenantHeader] = "acme" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Vary.Should().Contain(TenantHeader).And.Contain(LegacyTenantHeader);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(TenancyErrorCodes.IdentifierInvalid);

        store.IdentifierLookups.Should().Be(0);
        ConsultCountingStubSource.Consultations.Should().Be(0);
    }

    [Fact]
    public async Task should_resolve_from_a_custom_header_name_bound_from_configuration_and_drop_the_default()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Tenant:Header:HeaderNames:0"] = CustomTenantHeader }
            )
            .Build();
        await using var app = await _CreateAppAsync(sources =>
            sources.AddHeaderSource(configuration.GetSection("Tenant:Header"))
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var customResponse = await _SendAsync(
            client,
            new Dictionary<string, string> { [CustomTenantHeader] = "acme" }
        );
        using var defaultResponse = await _SendAsync(
            client,
            new Dictionary<string, string> { [TenantHeader] = "acme" }
        );

        customResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var custom = await customResponse.Content.ReadFromJsonAsync<TenantCatalogResponse>(
            cancellationToken: AbortToken
        );
        custom!.Id.Should().Be("ten_123");
        customResponse.Headers.Vary.Should().Contain(CustomTenantHeader).And.NotContain(TenantHeader);

        // The bound list replaces the untouched default: X-Tenant is no longer a way to select a tenant.
        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fallthrough = await defaultResponse.Content.ReadFromJsonAsync<TenantCatalogResponse>(
            cancellationToken: AbortToken
        );
        fallthrough!.Id.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_a_header_selected_tenant_when_the_authenticated_claim_names_another_tenant()
    {
        // The claim-mismatch check applies to header-selected tenants exactly as to any other source: the
        // test scheme turns X-Test-Tenant into the tenant-id claim, which disagrees with the acme catalog row.
        await using var app = await _CreateAppAsync(sources => sources.AddHeaderSource());
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(
            client,
            new Dictionary<string, string>
            {
                [TenantHeader] = "acme",
                [HttpTenancyTestHarness.UserHeader] = "alice",
                [HttpTenancyTestHarness.TenantHeader] = "ten_999",
            }
        );

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
    public async Task should_resolve_a_header_selected_tenant_for_a_principal_without_a_tenant_claim()
    {
        // Pins the documented limit: a principal with no tenant claim passes a source-selected
        // tenant unchecked — there is nothing to compare against.
        await using var app = await _CreateAppAsync(sources => sources.AddHeaderSource());
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(
            client,
            new Dictionary<string, string> { [TenantHeader] = "acme", [HttpTenancyTestHarness.UserHeader] = "alice" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant = await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken);
        tenant!.Id.Should().Be("ten_123");
    }

    [Fact]
    public async Task should_let_a_host_source_registered_first_win_without_consulting_the_header_source()
    {
        // First Found wins, so the header source never runs — and because it never ran, the
        // response must not vary on X-Tenant (Vary is stamped only on consult).
        await using var app = await _CreateAppAsync(sources =>
            sources.AddHostSource("{tenant}.example.com").AddHeaderSource()
        );
        using var client = HttpTenancyTestHarness.CreateClient(app);

        using var response = await _SendAsync(
            client,
            new Dictionary<string, string> { [TenantHeader] = "globex" },
            host: "acme.example.com"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenant = await response.Content.ReadFromJsonAsync<TenantCatalogResponse>(cancellationToken: AbortToken);
        tenant!.Id.Should().Be("ten_123");
        response.Headers.Vary.Should().NotContain(TenantHeader);
    }

    [Fact]
    public async Task should_fail_host_startup_when_a_header_name_is_not_an_http_token()
    {
        // The validator's message names the offending header and surfaces through ValidateOnStart.
        var act = () => _CreateAppAsync(sources => sources.AddHeaderSource("X Tenant"));

        (await act.Should().ThrowAsync<OptionsValidationException>()).WithMessage("*X Tenant*").WithMessage("*token*");
    }

    // --- app factory and helpers ---

    private async Task<WebApplication> _CreateAppAsync(
        Action<HeadlessTenantCatalogResolutionBuilder> configureSources,
        CountingTenantStore? store = null
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
                catalog.UseInMemory(o => o.Tenants.Add(new TenantInfo("ten_123", "acme", "Acme Inc", isEnabled: true)))
            );

            tenancy.Http(http => http.ResolveFromCatalog(configureSources));
        });

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
                Results.Json(new TenantCatalogResponse(currentTenant.Id, currentTenant.IsAvailable, currentTenant.Name))
        );

        await app.StartAsync(AbortToken);
        return app;
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        IReadOnlyDictionary<string, string>? headers,
        string? host = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.Add(name, value);
            }
        }

        return await client.SendAsync(request, AbortToken);
    }

    /// <summary>
    /// Writes an HTTP/1.0 request over a raw socket so repeated header lines reach Kestrel verbatim
    /// (<see cref="HttpClient"/> folds them into one comma-joined line). HTTP/1.0 keeps the response
    /// unchunked and connection-delimited, so the body is simply everything after the header block.
    /// </summary>
    private async Task<RawResponse> _SendRawAsync(WebApplication app, (string Name, string Value)[] headers)
    {
        var uri = new Uri(app.Urls.Single());
        var requestText = new StringBuilder()
            .Append("GET /tenant HTTP/1.0\r\n")
            .Append("Host: ")
            .Append(uri.Authority)
            .Append("\r\n");

        foreach (var (name, value) in headers)
        {
            requestText.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        requestText.Append("\r\n");

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(uri.Host, uri.Port, AbortToken);
        await using var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(requestText.ToString()), AbortToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, AbortToken);

        var responseText = Encoding.UTF8.GetString(buffer.ToArray());
        var headerEnd = responseText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        headerEnd.Should().BeGreaterThan(0, "the raw response must contain a header block");
        var lines = responseText[..headerEnd].Split("\r\n");
        var statusCode = int.Parse(lines[0].Split(' ')[1], CultureInfo.InvariantCulture);
        var parsedHeaders = lines
            .Skip(1)
            .Select(line =>
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);
                return (Name: line[..separator], Value: line[(separator + 1)..].Trim());
            })
            .ToList();

        return new RawResponse(statusCode, parsedHeaders, responseText[(headerEnd + 4)..]);
    }

    private static List<string> _VaryTokens(RawResponse response)
    {
        return response
            .Headers.Where(h => string.Equals(h.Name, "Vary", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value.Split(','))
            .Select(token => token.Trim())
            .ToList();
    }

    private sealed record RawResponse(int StatusCode, List<(string Name, string Value)> Headers, string Body);

    /// <summary>A later source whose consult count proves an Invalid result short-circuits the loop.</summary>
    private sealed class ConsultCountingStubSource : ITenantIdentifierSource
    {
        public static int Consultations;

        public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
        {
            Consultations++;
            return TenantIdentifierSourceResult.None;
        }
    }

    /// <summary>
    /// Replaces the in-memory store so a test can prove resolution never reached the catalog: every
    /// identifier lookup is counted, and the seeded acme tenant is returned so an incorrectly continued
    /// pipeline would resolve rather than 404.
    /// </summary>
    private sealed class CountingTenantStore : ITenantStore
    {
        private readonly TenantInfo _acme = new("ten_123", "acme", "Acme Inc", isEnabled: true);

        public int IdentifierLookups { get; private set; }

        public Task<TenantInfo?> FindByIdentifierAsync(
            string normalizedIdentifier,
            CancellationToken cancellationToken = default
        )
        {
            IdentifierLookups++;
            return Task.FromResult(normalizedIdentifier == "acme" ? _acme : null);
        }

        public Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(id == _acme.Id ? _acme : null);
        }
    }
}
