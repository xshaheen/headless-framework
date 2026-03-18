// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Endpoints;

public sealed class MonitoringEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task Stats_should_return_aggregate_counts()
    {
        // given
        var stats = new StatisticsView
        {
            PublishedSucceeded = 100,
            PublishedFailed = 5,
            ReceivedSucceeded = 200,
            ReceivedFailed = 10,
            Servers = 0,
        };

        _monitoringApi.GetStatisticsAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(stats));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/stats");

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("100"); // PublishedSucceeded
        body.Should().Contain("200"); // ReceivedSucceeded
    }

    [Fact]
    public async Task Nodes_should_return_empty_when_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/nodes");

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[]");
    }

    [Fact]
    public async Task Nodes_should_return_nodes_from_discovery_provider()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        var nodes = new List<Node>
        {
            new()
            {
                Id = "1",
                Name = "node1",
                Address = "10.0.0.1",
                Port = 8080,
                Tags = "web",
            },
            new()
            {
                Id = "2",
                Name = "node2",
                Address = "10.0.0.2",
                Port = 8080,
                Tags = "api",
            },
        };
        discoveryProvider.GetNodes().Returns(Task.FromResult<IList<Node>>(nodes));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage, discoveryProvider);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/nodes");

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("node1");
        body.Should().Contain("node2");
    }

    [Fact]
    public async Task ListNamespaces_should_return_namespaces_from_provider()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        var namespaces = new List<string> { "default", "staging" };
        discoveryProvider.GetNamespaces(Arg.Any<CancellationToken>()).Returns(Task.FromResult(namespaces));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage, discoveryProvider);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/list-ns");

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("default");
        body.Should().Contain("staging");
    }

    [Fact]
    public async Task ListNamespaces_should_return_404_when_discovery_returns_null()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNamespaces(Arg.Any<CancellationToken>()).Returns(Task.FromResult<List<string>>(null!));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage, discoveryProvider);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/list-ns");

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListServices_should_return_empty_when_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/list-svc/default");

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[]");
    }

    private static WebApplication _CreateTestApp(
        IDataStorage dataStorage,
        INodeDiscoveryProvider? discoveryProvider = null
    )
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();

        var appBuilder = WebApplication.CreateSlimBuilder();
        appBuilder.WebHost.UseTestServer();

        appBuilder.Services.AddSingleton(config);
        appBuilder.Services.AddSingleton(config.Auth);
        appBuilder.Services.AddScoped<IAuthService, AuthService>();
        appBuilder.Services.AddSingleton(dataStorage);
        appBuilder.Services.AddSingleton<MessagingMetricsEventListener>();

        // Register gateway proxy dependencies (must come before discoveryProvider override)
        _RegisterGatewayProxyDeps(appBuilder.Services);

        if (discoveryProvider != null)
        {
            // Override the mock INodeDiscoveryProvider from gateway deps
            appBuilder.Services.AddSingleton(discoveryProvider);
        }

        appBuilder.Services.AddRouting();
        appBuilder.Services.AddAuthorization();
        appBuilder.Services.AddCors(o => o.AddPolicy("Messaging_Dashboard_CORS", p => p.AllowAnyOrigin()));

        var app = appBuilder.Build();
        app.UseRouting();
        app.UseCors("Messaging_Dashboard_CORS");
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }

    /// <summary>
    /// Register GatewayProxyAgent and its dependencies so that
    /// ActivatorUtilities can resolve GatewayProxyEndpointFilter.
    /// </summary>
    private static void _RegisterGatewayProxyDeps(IServiceCollection services)
    {
        services.AddSingleton(Substitute.For<IRequestMapper>());
        services.AddSingleton(Substitute.For<IHttpClientFactory>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        services.AddSingleton<GatewayProxyAgent>();
    }
}
