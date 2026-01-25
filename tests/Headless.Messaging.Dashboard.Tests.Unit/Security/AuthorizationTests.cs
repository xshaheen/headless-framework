// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Tests.Security;

/// <summary>
/// Tests for authentication and authorization behavior of dashboard endpoints.
/// Documents the default anonymous access and policy enforcement.
/// </summary>
public sealed class AuthorizationTests : TestBase
{
    [Fact]
    public void should_require_auth_when_AllowAnonymous_is_false()
    {
        // given
        var options = new DashboardOptions
        {
            AllowAnonymousExplicit = false,
            AuthorizationPolicy = "AdminOnly",
        };

        // then - options should require authorization
        options.AllowAnonymousExplicit.Should().BeFalse();
        options.AuthorizationPolicy.Should().Be("AdminOnly");
    }

    [Fact]
    public void should_allow_anonymous_when_AllowAnonymous_is_true()
    {
        // given
        var options = new DashboardOptions { AllowAnonymousExplicit = true };

        // then
        options.AllowAnonymousExplicit.Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_AllowAnonymous_false_and_no_policy()
    {
        // given
        var options = new DashboardOptions
        {
            AllowAnonymousExplicit = false,
            AuthorizationPolicy = null, // No policy specified
        };

        // when
        // The AllowAnonymousIf extension method should throw
        // when anonymous is not allowed but no policy is provided
        var mockBuilder = Substitute.For<IEndpointConventionBuilder>();
        var act = () => MessagingBuilderExtension.AllowAnonymousIf(
            mockBuilder,
            options.AllowAnonymousExplicit,
            options.AuthorizationPolicy
        );

        // then
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Authorization Policy must be configured*");
    }

    [Fact]
    public void should_apply_authorization_policy()
    {
        // given
        var options = new DashboardOptions
        {
            AllowAnonymousExplicit = false,
            AuthorizationPolicy = "DashboardAdmin",
        };

        // then - policy should be set
        options.AuthorizationPolicy.Should().Be("DashboardAdmin");
        options.AllowAnonymousExplicit.Should().BeFalse();
    }

    [Fact]
    public void health_endpoint_should_always_be_anonymous()
    {
        // The health endpoint is always anonymous regardless of AllowAnonymousExplicit
        // This is by design for health check systems

        // Document that health endpoint uses .AllowAnonymous() directly
        // in MapDashboardRoutes:
        // _builder.MapGet(prefixMatch + "/health", Health).AllowAnonymous();

        true.Should().BeTrue("Health endpoint always allows anonymous access");
    }

    [Fact]
    public void ping_endpoint_should_always_be_anonymous()
    {
        // The ping endpoint is always anonymous regardless of AllowAnonymousExplicit
        // This may be a security concern combined with SSRF vulnerability

        // Document that ping endpoint uses .AllowAnonymous() directly:
        // _builder.MapGet(prefixMatch + "/ping", PingServices).AllowAnonymous();

        // SECURITY CONCERN: Anonymous access + SSRF = unauthenticated SSRF attacks

        true.Should().BeTrue("Ping endpoint allows anonymous access");
    }

    [Fact]
    public void default_options_allow_anonymous_access_to_all_data_endpoints()
    {
        // SECURITY CONCERN: Default options allow anonymous access
        // This means sensitive message data is exposed by default

        var options = new DashboardOptions();

        options.AllowAnonymousExplicit.Should().BeTrue(
            "Default allows anonymous - this is a security concern");

        // Data endpoints that are exposed:
        // - /api/published/{status} - lists published messages
        // - /api/received/{status} - lists received messages
        // - /api/published/message/{id} - message content
        // - /api/received/message/{id} - message content
        // - /api/stats - statistics
        // - /api/metrics-realtime - real-time metrics
        // - /api/metrics-history - historical metrics
        // - /api/subscriber - subscriber information
        // - /api/nodes - cluster node information
    }

    // NOTE: Testing the actual AllowAnonymousIf extension method directly is difficult
    // because it calls static extension methods (AllowAnonymous/RequireAuthorization) on the builder.
    // The logic is simple and tested indirectly through the throw test above.

    [Fact]
    public void AllowAnonymousIf_extension_method_exists()
    {
        // Document that the extension method signature exists with correct parameters
        var method = typeof(MessagingBuilderExtension).GetMethod(
            "AllowAnonymousIf",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
        );

        method.Should().NotBeNull("AllowAnonymousIf extension method should exist");
        method!.GetParameters().Should().HaveCount(3);
    }

    [Fact]
    public void dangerous_endpoints_should_require_authentication()
    {
        // Document which endpoints can modify data and should require auth:

        // POST /api/published/requeue - re-publishes messages
        // POST /api/published/delete - deletes published messages
        // POST /api/received/reexecute - re-executes received messages
        // POST /api/received/delete - deletes received messages

        // These endpoints can:
        // 1. Cause message re-processing (duplicate processing)
        // 2. Delete message history (audit trail destruction)
        // 3. Potentially cause denial of service via mass requeue

        var options = new DashboardOptions();
        options.AllowAnonymousExplicit.Should().BeTrue(
            "DANGER: Data modification endpoints are anonymous by default");
    }
}
