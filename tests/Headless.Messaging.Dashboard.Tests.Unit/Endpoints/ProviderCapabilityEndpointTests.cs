// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Dashboard.Authentication;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class ProviderCapabilityEndpointTests : TestBase
{
    [Fact]
    public async Task should_report_runtime_provider_capabilities_when_meta()
    {
        await using var app = _CreateTestApp();
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/api/meta", AbortToken);

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var capabilities = document.RootElement.GetProperty("providerCapabilities").EnumerateArray().ToArray();
        capabilities.Should().ContainSingle();
        capabilities[0].GetProperty("provider").GetString().Should().Be("Test Transport");
        capabilities[0].GetProperty("role").GetString().Should().Be("Transport");
        capabilities[0]
            .GetProperty("lanes")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .BeEquivalentTo(["Bus", "Queue"]);
        capabilities[0].GetProperty("supportsIndependentLaneTopology").GetBoolean().Should().BeTrue();
    }

    private static WebApplication _CreateTestApp()
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Auth);
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        builder.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        builder.Services.AddSingleton<MessagingDashboardCache>();
        builder.Services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        builder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        builder.Services.AddSingleton<GatewayProxyAgent>();
        builder.Services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Transport(
                "Test Transport",
                [MessageLane.Bus, MessageLane.Queue],
                supportsIndependentLaneTopology: true
            )
        );
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddCors(options =>
            options.AddPolicy("HeadlessMessagingDashboardCORS", policy => policy.AllowAnyOrigin())
        );

        var app = builder.Build();
        app.UseRouting();
        app.UseCors("HeadlessMessagingDashboardCORS");
        app.MapMessagingDashboardEndpoints(config);
        return app;
    }
}
