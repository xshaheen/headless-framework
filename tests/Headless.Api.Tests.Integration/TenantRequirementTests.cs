// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TenantRequirementTests : TestBase
{
    private const string _Scheme = "Test";
    private const string _UserHeader = "X-Test-User";
    private const string _TenantHeader = "X-Test-Tenant";

    [Fact]
    public async Task should_allow_authenticated_request_with_tenant_claim()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-required", user: "alice", tenantId: "tenant-a");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantResponse>(cancellationToken: AbortToken);
        body!.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task should_return_tenant_problem_details_when_authenticated_request_has_no_tenant_claim()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-required", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _AssertTenantRequiredProblemDetailsAsync(response);
    }

    [Fact]
    public async Task should_allow_minimal_api_endpoint_that_allows_missing_tenant()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/minimal-allow-missing", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_require_tenant_when_minimal_api_endpoint_overrides_group_allow_missing()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-requirement-group/require-tenant", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _AssertTenantRequiredProblemDetailsAsync(response);
    }

    [Fact]
    public async Task should_allow_mvc_action_that_allows_missing_tenant()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-requirement-mvc/allow-missing", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_allow_mvc_controller_that_allows_missing_tenant()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-requirement-public/class-allow-missing", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_require_tenant_when_mvc_action_overrides_controller_allow_missing()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-requirement-public/action-require", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _AssertTenantRequiredProblemDetailsAsync(response);
    }

    [Fact]
    public async Task should_map_missing_tenant_context_exception_to_tenant_problem_details()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/throw-missing-tenant", user: "alice", tenantId: "tenant-a");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _AssertTenantRequiredProblemDetailsAsync(response);
    }

    [Fact]
    public async Task should_return_401_for_unauthenticated_tenant_required_endpoint()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-required");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_return_tenant_problem_details_when_composed_policy_also_fails()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-and-denied", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _AssertTenantRequiredProblemDetailsAsync(response);
    }

    [Fact]
    public async Task should_delegate_to_default_forbidden_when_composed_policy_tenant_passes()
    {
        await using var app = await _CreateAppAsync();
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-and-denied", user: "alice", tenantId: "tenant-a");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().NotContain(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
    }

    [Fact]
    public async Task should_apply_customize_problem_details_to_auth_path_tenant_response()
    {
        // Both the auth-path (this test) and exception-path (next test) responses must include the
        // consumer's CustomizeProblemDetails customizations because both routes now go through
        // IProblemDetailsService.TryWriteAsync.
        await using var app = await _CreateAppAsync(configureProblemDetails: options =>
            options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["x-custom"] = "auth-marker"
        );
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/tenant-required", user: "alice");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("x-custom").GetString().Should().Be("auth-marker");
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
    }

    [Fact]
    public async Task should_apply_customize_problem_details_to_exception_path_tenant_response()
    {
        await using var app = await _CreateAppAsync(configureProblemDetails: options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["x-custom"] = "exception-marker"
        );
        using var client = _CreateClient(app);

        using var response = await _SendAsync(client, "/throw-missing-tenant", user: "alice", tenantId: "tenant-a");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("x-custom").GetString().Should().Be("exception-marker");
        doc.RootElement.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
    }

    private async Task<WebApplication> _CreateAppAsync(Action<ProblemDetailsOptions>? configureProblemDetails = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
        );

        if (configureProblemDetails is not null)
        {
            builder.Services.Configure(configureProblemDetails);
        }
        builder.Services.AddSingleton<IAuthorizationHandler, AlwaysFailRequirementHandler>();
        builder
            .Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = _Scheme;
                options.DefaultChallengeScheme = _Scheme;
                options.DefaultForbidScheme = _Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(_Scheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(_Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantRequirement())
                .Build();
            options.AddPolicy(
                "TenantAndDenied",
                policy =>
                    policy
                        .RequireAuthenticatedUser()
                        .AddRequirements(new TenantRequirement(), new AlwaysFailRequirement())
            );
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(TenantRequirementController).Assembly);

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseHeadlessTenancy();
        app.UseAuthorization();

        app.MapGet(
            "/tenant-required",
            (ICurrentTenant currentTenant) => Results.Json(new TenantResponse(currentTenant.Id))
        );
        app.MapGet("/minimal-allow-missing", () => Results.Ok()).AllowMissingTenant();
        var allowMissingGroup = app.MapGroup("/tenant-requirement-group").AllowMissingTenant();
        allowMissingGroup.MapGet("/require-tenant", () => Results.Ok()).RequireTenant();
        app.MapGet("/throw-missing-tenant", (Action)(() => throw new MissingTenantContextException()));
        app.MapGet("/tenant-and-denied", () => Results.Ok()).RequireAuthorization("TenantAndDenied");
        app.MapControllers();

        await app.StartAsync(AbortToken);
        return app;
    }

    private static HttpClient _CreateClient(WebApplication app)
    {
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }

    private async Task<HttpResponseMessage> _SendAsync(
        HttpClient client,
        string path,
        string? user = null,
        string? tenantId = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (user is not null)
        {
            request.Headers.Add(_UserHeader, user);
        }

        if (tenantId is not null)
        {
            request.Headers.Add(_TenantHeader, tenantId);
        }

        return await client.SendAsync(request, AbortToken);
    }

    private async Task _AssertTenantRequiredProblemDetailsAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var json = await response.Content.ReadAsStringAsync(AbortToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status403Forbidden);
        root.GetProperty("title").GetString().Should().Be(HeadlessProblemDetailsConstants.Titles.Forbidden);
        root.GetProperty("detail")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Details.TenantContextRequired);
        root.GetProperty("error")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(HeadlessProblemDetailsConstants.Errors.TenantContextRequired.Code);
    }

    private static void _AddDefaultHeadlessSecurityConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
            new KeyValuePair<string, string?>("Headless:StringHash:DefaultSalt", "TestSalt"),
        ]);
    }

    private sealed record TenantResponse(string? TenantId);

    private sealed class AlwaysFailRequirement : IAuthorizationRequirement;

    private sealed class AlwaysFailRequirementHandler : AuthorizationHandler<AlwaysFailRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            AlwaysFailRequirement requirement
        )
        {
            context.Fail(new AuthorizationFailureReason(this, "AlwaysFail"));

            return Task.CompletedTask;
        }
    }

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

            var claims = new List<Claim> { new(UserClaimTypes.Name, userValues.ToString()) };

            if (Request.Headers.TryGetValue(_TenantHeader, out var tenantValues))
            {
                claims.Add(new Claim(UserClaimTypes.TenantId, tenantValues.ToString()));
            }

            var identity = new ClaimsIdentity(claims, authenticationType: Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

[ApiController]
[Route("tenant-requirement-mvc")]
public sealed class TenantRequirementController : ControllerBase
{
    [HttpGet("allow-missing")]
    [AllowMissingTenant]
    public IActionResult AllowMissing()
    {
        return Ok();
    }
}

[ApiController]
[AllowMissingTenant]
[Route("tenant-requirement-public")]
public sealed class PublicTenantRequirementController : ControllerBase
{
    [HttpGet("class-allow-missing")]
    public IActionResult ClassAllowMissing()
    {
        return Ok();
    }

    [HttpGet("action-require")]
    [RequireTenant]
    public IActionResult ActionRequire()
    {
        return Ok();
    }
}
