// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

/// <summary>
/// Regression coverage for the default (unconfigured) CORS pipeline. Dashboard endpoints carry
/// <c>RequireCors</c> metadata; if the CORS middleware is skipped when no policy was configured, ASP.NET
/// Core throws at request time for every endpoint instead of just falling back to same-origin. Unlike
/// <see cref="HealthEndpointTests"/> (which hand-wires <c>UseCors</c> directly), this test goes through the
/// real production pipeline (<c>SetupMessagingDashboard.UseMessagingDashboard</c>) so it actually exercises
/// the conditional this bug lived in.
/// </summary>
public sealed class DefaultCorsPipelineTests : TestBase
{
    [Fact]
    public async Task Health_endpoint_should_not_500_when_no_cors_policy_is_configured()
    {
        // given - a builder with no SetCorsOrigins / SetCorsPolicy call (the shipped default)
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();
        config.CorsPolicyBuilder.Should().BeNull();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Auth);
        builder.Services.AddScoped<IAuthService, AuthService>();

        // The endpoint data source eagerly binds every mapped endpoint's metadata (not just the one under
        // test) the first time authorization/routing initializes, so the other endpoints' DI dependencies
        // (e.g. the gateway proxy's IHttpClientFactory) must resolve even though this test never calls them.
        builder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        builder.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        builder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        builder.Services.AddSingleton<GatewayProxyAgent>();

        builder.Services.AddRouting();
        builder.Services.AddAuthorization();

        // Mirrors DashboardOptionsExtensions' production registration: always register the named policy,
        // falling back to an empty (same-origin-only) policy when the consumer configured none.
        builder.Services.AddCors(options =>
            options.AddPolicy("HeadlessMessagingDashboardCORS", config.CorsPolicyBuilder ?? (static _ => { }))
        );

        await using var app = builder.Build();

        // Exercise the real production pipeline entrypoint, not a hand-wired stand-in.
        app.UseMessagingDashboard(config);

        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/messaging/api/health", AbortToken);

        // then - must succeed (same-origin request, no CORS header needed) rather than 500
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Be("OK");
    }
}
