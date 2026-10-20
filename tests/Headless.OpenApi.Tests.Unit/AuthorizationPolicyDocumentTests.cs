// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Documents the policies and limiters that apply to an endpoint without appearing in its metadata.</summary>
public sealed class AuthorizationPolicyDocumentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync(Action<IServiceCollection> configure) =>
        OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                configure(builder.Services);
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
            {
                app.MapGet("/plain", () => TypedResults.Ok("plain"));
                app.MapGet("/anonymous", () => TypedResults.Ok("anonymous")).AllowAnonymous();
                app.MapGet("/optional-tenant", () => TypedResults.Ok("optional")).AllowMissingTenant();
                app.MapGet("/authorized", () => TypedResults.Ok("authorized")).RequireAuthorization();
                app.MapGet("/unlimited", () => TypedResults.Ok("unlimited")).DisableRateLimiting();
                app.MapGet("/requirement-data", () => TypedResults.Ok("requirement-data"))
                    .WithMetadata(new RequireAuditScopeAttribute());
            },
            "v1",
            AbortToken
        );

    [Fact]
    public async Task should_document_the_fallback_policy_on_endpoints_without_authorization_metadata()
    {
        var document = await _GetDocumentAsync(services =>
            services.Configure<AuthorizationOptions>(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new TenantRequirement())
                    .Build()
            )
        );

        var plain = document.Operation("/plain", "get");
        _ResponseCodes(plain).Should().Equal("200", "401", "403", "404", "409", "422");
        plain["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");
        plain["responses"]!["403"]!["content"]!["application/problem+json"]!["examples"]!
            .AsObject()
            .Select(x => x.Key)
            .Should()
            .Equal("forbidden", "tenantRequired");

        // The handler lets AllowMissingTenant endpoints through, so only authentication can still fail.
        _ResponseCodes(document.Operation("/optional-tenant", "get"))
            .Should()
            .Equal("200", "401", "404", "409", "422");

        var anonymous = document.Operation("/anonymous", "get");
        _ResponseCodes(anonymous).Should().Equal("200", "404", "409", "422");
        anonymous["security"].Should().BeNull();

        // A bare RequireAuthorization() takes the default policy instead, which only authenticates.
        _ResponseCodes(document.Operation("/authorized", "get")).Should().Equal("200", "401", "404", "409", "422");
    }

    [Fact]
    public async Task should_document_the_default_policy_of_a_bare_authorization_requirement()
    {
        var document = await _GetDocumentAsync(services =>
            services.Configure<AuthorizationOptions>(options =>
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new TenantRequirement())
                    .Build()
            )
        );

        var authorized = document.Operation("/authorized", "get");
        _ResponseCodes(authorized).Should().Equal("200", "401", "403", "404", "409", "422");
        authorized["responses"]!["403"]!["content"]!["application/problem+json"]!["examples"]!["tenantRequired"]![
            "value"
        ]!["error"]!
            .ToJsonString()
            .Should()
            .Be("""{"code":"g:tenant_required","description":"This operation requires a tenant, but none was set."}""");

        // No fallback policy: an endpoint without authorization metadata stays anonymous.
        _ResponseCodes(document.Operation("/plain", "get")).Should().Equal("200", "404", "409", "422");
        document.Operation("/plain", "get")["security"].Should().BeNull();
    }

    [Fact]
    public async Task should_document_the_global_limiter_rejection_with_the_configured_status()
    {
        var document = await _GetDocumentAsync(services =>
            services.AddRateLimiter(options =>
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetNoLimiter("all")
                )
            )
        );

        // Without UseHeadlessProblemDetails() the native rejection status, 503, applies.
        var plain = document.Operation("/plain", "get");
        _ResponseCodes(plain).Should().Equal("200", "503", "404", "409", "422");
        ((string?)plain["responses"]!["503"]!["content"]!["application/problem+json"]!["schema"]!["$ref"])
            .Should()
            .Be("#/components/schemas/ServiceUnavailableProblemDetails");

        _ResponseCodes(document.Operation("/unlimited", "get")).Should().Equal("200", "404", "409", "422");
    }

    [Fact]
    public async Task should_document_authorization_requirement_data_as_able_to_challenge_and_forbid()
    {
        var document = await _GetDocumentAsync(_ => { });
        var operation = document.Operation("/requirement-data", "get");

        _ResponseCodes(operation).Should().Equal("200", "401", "403", "404", "409", "422");
        operation["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");
    }

    private static string[] _ResponseCodes(JsonNode operation) => operation["responses"]!.ExactStatusCodes();
}

/// <summary>An attribute that carries its own requirement, which the authorization middleware enforces.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequireAuditScopeAttribute : Attribute, IAuthorizationRequirementData
{
    public IEnumerable<IAuthorizationRequirement> GetRequirements() =>
        [new ClaimsAuthorizationRequirement("scope", ["orders.audit"])];
}
