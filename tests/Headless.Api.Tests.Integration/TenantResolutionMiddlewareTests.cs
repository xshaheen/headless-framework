// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Middlewares;
using Headless.Constants;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TenantResolutionMiddlewareTests : TestBase
{
    private const string _Scheme = "Test";
    private const string _UserHeader = "X-Test-User";
    private const string _TenantHeader = "X-Test-Tenant";
    private const string _CustomTenantHeader = "X-Test-Custom-Tenant";
    private const string _UnauthenticatedHeader = "X-Test-Unauthenticated";

    [Fact]
    public async Task should_resolve_default_tenant_claim_and_not_bleed_between_requests()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var authenticated = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");
        var unauthenticated = await _GetTenantAsync(client);
        var missingClaim = await _GetTenantAsync(client, user: "alice");

        authenticated.Id.Should().Be("TENANT-1");
        authenticated.IsAvailable.Should().BeTrue();
        unauthenticated.Id.Should().BeNull();
        unauthenticated.IsAvailable.Should().BeFalse();
        missingClaim.Id.Should().BeNull();
        missingClaim.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_resolve_custom_tenant_claim_type_when_configured()
    {
        await using var app = await _CreateAppAsync(options => options.ClaimType = "custom_tenant_id");
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", customTenantId: "TENANT-42");

        tenant.Id.Should().Be("TENANT-42");
        tenant.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task should_skip_resolution_for_unauthenticated_principal_even_when_tenant_claim_exists()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1", unauthenticated: true);

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_skip_resolution_for_whitespace_tenant_claim()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "   ");

        tenant.Id.Should().BeNull();
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task should_fall_back_to_default_claim_type_when_custom_claim_type_is_blank()
    {
        await using var app = await _CreateAppAsync(options => options.ClaimType = " ");
        using var client = _CreateClient(app);

        var tenant = await _GetTenantAsync(client, user: "alice", tenantId: "TENANT-1");

        tenant.Id.Should().Be("TENANT-1");
        tenant.IsAvailable.Should().BeTrue();
    }

    private async Task<WebApplication> _CreateAppAsync(Action<MultiTenancyOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.AddHeadlessApi(_ConfigureEncryption);

        if (configure is not null)
        {
            builder.AddHeadlessMultiTenancy(configure);
        }

        builder
            .Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = _Scheme;
                options.DefaultChallengeScheme = _Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(_Scheme, _ => { });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseTenantResolution();
        app.UseAuthorization();

        app.MapGet("/tenant", (ICurrentTenant currentTenant) => Results.Json(new TenantResponse(currentTenant.Id, currentTenant.IsAvailable)));

        await app.StartAsync(AbortToken);

        return app;
    }

    private HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private async Task<TenantResponse> _GetTenantAsync(
        HttpClient client,
        string? user = null,
        string? tenantId = null,
        string? customTenantId = null,
        bool unauthenticated = false
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");

        if (user is not null)
        {
            request.Headers.Add(_UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(_TenantHeader, tenantId);
        }

        if (customTenantId is not null)
        {
            request.Headers.Add(_CustomTenantHeader, customTenantId);
        }

        if (unauthenticated)
        {
            request.Headers.Add(_UnauthenticatedHeader, "true");
        }

        using var response = await client.SendAsync(request, AbortToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TenantResponse>(cancellationToken: AbortToken))!;
    }

    private static void _ConfigureEncryption(StringEncryptionOptions options)
    {
        options.DefaultPassPhrase = "TestPassPhrase123456";
        options.InitVectorBytes = "TestIV0123456789"u8.ToArray();
        options.DefaultSalt = "TestSalt"u8.ToArray();
    }

    private sealed record TenantResponse(string? Id, bool IsAvailable);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(_UserHeader, out var userValues))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var isUnauthenticated = Request.Headers.ContainsKey(_UnauthenticatedHeader);
            var claims = new List<Claim> { new(UserClaimTypes.Name, userValues.ToString()) };

            if (Request.Headers.TryGetValue(_TenantHeader, out var tenantValues))
            {
                claims.Add(new Claim(UserClaimTypes.TenantId, tenantValues.ToString()));
            }

            if (Request.Headers.TryGetValue(_CustomTenantHeader, out var customTenantValues))
            {
                claims.Add(new Claim("custom_tenant_id", customTenantValues.ToString()));
            }

            var identity = new ClaimsIdentity(claims, authenticationType: isUnauthenticated ? null : Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
