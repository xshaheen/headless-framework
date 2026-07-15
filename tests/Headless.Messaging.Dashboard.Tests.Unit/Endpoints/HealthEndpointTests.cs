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

public sealed class HealthEndpointTests : TestBase
{
    [Fact]
    public async Task should_return_ok_when_health()
    {
        // given
        await using var app = _CreateTestApp();
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/health", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Be("OK");
    }

    [Fact]
    public async Task should_not_require_authentication_when_health()
    {
        // given - no auth header set
        await using var app = _CreateTestApp();
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/health", AbortToken);

        // then - should succeed without auth
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Be("OK");
    }

    private static WebApplication _CreateTestApp()
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Auth);
        builder.Services.AddScoped<IAuthService, AuthService>();
        // Gateway proxy deps for ActivatorUtilities resolution
        builder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        builder.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        builder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        builder.Services.AddSingleton<GatewayProxyAgent>();

        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddCors(o => o.AddPolicy("HeadlessMessagingDashboardCORS", p => p.AllowAnyOrigin()));

        var app = builder.Build();
        app.UseRouting();
        app.UseCors("HeadlessMessagingDashboardCORS");
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }
}
