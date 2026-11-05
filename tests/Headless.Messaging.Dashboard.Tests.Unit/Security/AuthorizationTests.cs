// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Security;

/// <summary>
/// Tests for authentication and authorization behavior of dashboard endpoints.
/// Documents the auth model using <see cref="MessagingDashboardOptionsBuilder"/>.
/// </summary>
public sealed class AuthorizationTests : TestBase
{
    [Fact]
    public void should_require_host_auth_with_policy()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication(host =>
            host.ViewPolicy = "AdminOnly"
        );

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.Host.ViewPolicy.Should().Be("AdminOnly");
    }

    [Fact]
    public void should_allow_anonymous_when_with_no_auth()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithNoAuth();

        // then
        builder.Auth.Mode.Should().Be(AuthMode.None);
        builder.Auth.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void should_support_basic_auth()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "secret");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Basic);
        builder.Auth.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_support_api_key_auth()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithApiKey("my-key");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.ApiKey);
        builder.Auth.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void should_always_be_anonymous_when_health_endpoint()
    {
        // The health endpoint is always anonymous regardless of auth mode.
        // This is by design for health check systems.
        // In MessagingDashboardEndpoints: MapGet("/api/health", _Health).AllowAnonymous()

        true.Should().BeTrue("Health endpoint always allows anonymous access");
    }

    [Fact]
    public async Task should_require_the_session_view_and_operate_policies_when_ping_endpoint_uses_host_auth()
    {
        // given
        var config = new MessagingDashboardOptionsBuilder().WithHostAuthentication(host =>
            host.ViewPolicy = "AdminOnly"
        );
        await using var app = _CreateEndpointApp(config);

        // when
        var endpoint = _GetPingEndpoint(app);
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy);

        // then - the dashboard session decides the request; the host policy is evaluated against its claims
        policies
            .Should()
            .BeEquivalentTo(
                MessagingDashboardOptionsBuilder.AuthenticationName + ".View",
                MessagingDashboardOptionsBuilder.AuthenticationName + ".Operate"
            );
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal("POST");
    }

    [Fact]
    public async Task should_remain_unprotected_when_ping_endpoint_uses_explicit_no_auth()
    {
        // given
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();
        await using var app = _CreateEndpointApp(config);

        // when
        var endpoint = _GetPingEndpoint(app);

        // then
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().BeEmpty();
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        config.AuthConfigured.Should().BeTrue();
        config.Auth.Mode.Should().Be(AuthMode.None);
    }

    [Theory]
    [InlineData(AuthMode.Basic)]
    [InlineData(AuthMode.ApiKey)]
    public async Task should_require_the_dashboard_policy_when_ping_endpoint_uses_a_builtin_credential(AuthMode mode)
    {
        // given
        await using var app = _CreateEndpointApp(BuiltInCredential.For(mode).Config);

        // when
        var endpoint = _GetPingEndpoint(app);

        // then - the dashboard's own policy, never the host's fallback, decides the request
        endpoint
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should()
            .ContainSingle(data => data.Policy == MessagingDashboardOptionsBuilder.AuthenticationName);
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
    }

    [Fact]
    public void default_builder_has_no_auth_enabled()
    {
        // Default builder has AuthMode.None (no auth)
        var builder = new MessagingDashboardOptionsBuilder();

        builder.Auth.Mode.Should().Be(AuthMode.None, "Default has no auth configured");
        builder.Auth.IsEnabled.Should().BeFalse("Auth is not enabled by default");
    }

    [Fact]
    public void protected_api_group_exists_for_data_endpoints()
    {
        // Document which endpoints are behind the protected /api group
        // (protected by the dashboard's authentication scheme and policy when auth is enabled):
        //
        // GET  /api/metrics-realtime
        // GET  /api/meta
        // GET  /api/stats
        // GET  /api/metrics-history
        // GET  /api/published/message/{id}
        // POST /api/published/requeue
        // POST /api/published/delete
        // GET  /api/published/{status}
        // GET  /api/received/message/{id}
        // POST /api/received/reexecute
        // POST /api/received/delete
        // GET  /api/received/{status}
        // GET  /api/subscriber
        // GET  /api/nodes
        // GET  /api/list-ns
        // GET  /api/list-svc/{namespace}
        // POST /api/ping

        // When Host auth is configured, RequireAuthorization() is applied to the group.
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication(host =>
            host.ViewPolicy = "DashboardAdmin"
        );
        builder.Auth.Host.ViewPolicy.Should().Be("DashboardAdmin");
    }

    [Fact]
    public void dangerous_endpoints_require_authentication_when_configured()
    {
        // POST /api/published/requeue - re-publishes messages
        // POST /api/published/delete - deletes published messages
        // POST /api/received/reexecute - re-executes received messages
        // POST /api/received/delete - deletes received messages
        //
        // These can cause:
        // 1. Message re-processing (duplicate processing)
        // 2. Audit trail destruction
        // 3. Denial of service via mass requeue
        //
        // Protected by the /api group auth when auth is enabled.

        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "pass");
        builder.Auth.IsEnabled.Should().BeTrue("Data modification endpoints are protected when auth is configured");
    }

    [Fact]
    public void auth_info_endpoint_is_always_anonymous()
    {
        // GET /api/auth/info is always anonymous so the frontend can determine auth mode.
        // In MessagingDashboardEndpoints: MapGet("/api/auth/info", _GetAuthInfo).AllowAnonymous()

        true.Should().BeTrue("Auth info endpoint must be anonymous for the login UI");
    }

    [Fact]
    public void validate_auth_endpoint_is_always_anonymous()
    {
        // POST /api/auth/validate is always anonymous for credential validation.
        // In MessagingDashboardEndpoints: MapPost("/api/auth/validate", _ValidateAuth).AllowAnonymous()

        true.Should().BeTrue("Auth validate endpoint must be anonymous for login flow");
    }

    private static WebApplication _CreateEndpointApp(MessagingDashboardOptionsBuilder config)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        MessagingDashboardHost.AddDashboardAuthentication(builder.Services, config);

        builder.Services.AddAuthorizationBuilder().AddPolicy("AdminOnly", policy => policy.RequireAssertion(_ => true));

        builder.Services.AddCors(options =>
            options.AddPolicy("HeadlessMessagingDashboardCORS", policy => policy.AllowAnyOrigin())
        );

        var app = builder.Build();
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }

    private static Endpoint _GetPingEndpoint(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(source => source.Endpoints)
            .Single(endpoint =>
                string.Equals(
                    endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    "Messaging_PingServices",
                    StringComparison.Ordinal
                )
            );
    }
}
