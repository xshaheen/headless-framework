// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Endpoints;

public sealed class MonitoringEndpointTests : TestBase
{
    private readonly IMonitoringApi _monitoringApi = Substitute.For<IMonitoringApi>();
    private readonly IDataStorage _dataStorage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task should_return_aggregate_counts_when_stats()
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
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/stats", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("100"); // PublishedSucceeded
        body.Should().Contain("200"); // ReceivedSucceeded
    }

    [Fact]
    public async Task should_return_bounded_read_only_unknown_lane_diagnostics()
    {
        // given
        UnknownLaneMessageQuery? capturedQuery = null;
        var storageId = Guid.Parse("11111111-1111-1111-1111-111111111350");
        var page = new IndexPage<UnknownLaneMessageView>(
            [
                new()
                {
                    StorageId = storageId,
                    MessageType = MessageType.Subscribe,
                    RawLane = 99,
                    Name = "poison-row",
                    StatusName = StatusName.Failed,
                    Added = DateTimeOffset.Parse("2026-07-26T08:00:00Z", CultureInfo.InvariantCulture),
                    NextRetryAt = DateTimeOffset.Parse("2026-07-26T08:05:00Z", CultureInfo.InvariantCulture),
                    LockedUntil = null,
                },
            ],
            index: 0,
            size: 200,
            totalItems: 1
        );
        _monitoringApi
            .GetUnknownLaneMessagesAsync(
                Arg.Do<UnknownLaneMessageQuery>(query => capturedQuery = query),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(page));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync(
            "/api/unknown-lanes?messageType=Subscribe&currentPage=0&perPage=500",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedQuery.Should().NotBeNull();
        capturedQuery!.MessageType.Should().Be(MessageType.Subscribe);
        capturedQuery.CurrentPage.Should().Be(1);
        capturedQuery.PageSize.Should().Be(200);

        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain(storageId.ToString("D")).And.Contain("\"rawLane\":99");
        body.Contains("content", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_empty_when_nodes_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/nodes", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Be("[]");
    }

    [Fact]
    public async Task should_return_nodes_from_discovery_provider_when_nodes()
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
        discoveryProvider
            .GetNodesAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<Node>>(nodes));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage, discoveryProvider);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/nodes", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("node1");
        body.Should().Contain("node2");
    }

    [Fact]
    public async Task should_return_namespaces_from_provider_when_list_namespaces()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        var namespaces = new List<string> { "default", "staging" };
        discoveryProvider.GetNamespacesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(namespaces));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage, discoveryProvider);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/list-ns", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("default");
        body.Should().Contain("staging");
    }

    [Fact]
    public async Task should_return_404_when_list_namespaces_discovery_returns_null()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider
            .GetNamespacesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<List<string>>(null!));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage, discoveryProvider);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/list-ns", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_return_empty_when_list_services_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        await using var app = _CreateTestApp(_dataStorage);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync("/api/list-svc/default", AbortToken);

        // then
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(AbortToken);
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
        // Stats endpoint resolves MethodMatcherCache to compute subscriber count.
        appBuilder.Services.AddSingleton(Substitute.For<IConsumerServiceSelector>());
        appBuilder.Services.AddSingleton<MethodMatcherCache>();

        // Register gateway proxy dependencies (must come before discoveryProvider override)
        _RegisterGatewayProxyDeps(appBuilder.Services);

        if (discoveryProvider != null)
        {
            // Override the mock INodeDiscoveryProvider from gateway deps
            appBuilder.Services.AddSingleton(discoveryProvider);
        }

        appBuilder.Services.AddRouting();
        appBuilder.Services.AddAuthorization();
        appBuilder.Services.AddCors(o => o.AddPolicy("HeadlessMessagingDashboardCORS", p => p.AllowAnyOrigin()));

        var app = appBuilder.Build();
        app.UseRouting();
        app.UseCors("HeadlessMessagingDashboardCORS");
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
        services.AddSingleton<MessagingDashboardCache>();
        services.AddSingleton(Substitute.For<INodeDiscoveryProvider>());
        services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "test-node" });
        services.AddSingleton<GatewayProxyAgent>();
    }
}
