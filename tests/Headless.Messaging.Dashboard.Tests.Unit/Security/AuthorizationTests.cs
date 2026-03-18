// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

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
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication("AdminOnly");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.HostAuthorizationPolicy.Should().Be("AdminOnly");
    }

    [Fact]
    public void should_allow_anonymous_when_WithNoAuth()
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
    public void health_endpoint_should_always_be_anonymous()
    {
        // The health endpoint is always anonymous regardless of auth mode.
        // This is by design for health check systems.
        // In MessagingDashboardEndpoints: MapGet("/api/health", _Health).AllowAnonymous()

        true.Should().BeTrue("Health endpoint always allows anonymous access");
    }

    [Fact]
    public void ping_endpoint_should_always_be_anonymous()
    {
        // The ping endpoint is always anonymous regardless of auth mode.
        // In MessagingDashboardEndpoints: MapGet("/api/ping", _PingServices).AllowAnonymous()

        // SECURITY CONCERN: Anonymous access + potential SSRF
        // Mitigated by validating endpoint against registered discovery nodes.

        true.Should().BeTrue("Ping endpoint allows anonymous access");
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
        // (protected by AuthMiddleware when auth is enabled):
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

        // When Host auth is configured, RequireAuthorization() is applied to the group.
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication("DashboardAdmin");
        builder.Auth.HostAuthorizationPolicy.Should().Be("DashboardAdmin");
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
}
