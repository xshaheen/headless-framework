// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

        var context = _CreateHttpContext(_dataStorage);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.Stats(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Contain("100"); // PublishedSucceeded
        response.Should().Contain("200"); // ReceivedSucceeded
    }

    [Fact]
    public async Task Nodes_should_return_empty_when_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        // No INodeDiscoveryProvider registered
        var context = _CreateHttpContext(_dataStorage);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.Nodes(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Be("[]");
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

        var context = _CreateHttpContext(_dataStorage, discoveryProvider);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.Nodes(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Contain("node1");
        response.Should().Contain("node2");
    }

    [Fact]
    public async Task ListNamespaces_should_return_empty_when_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ListNamespaces(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Be("[]");
    }

    [Fact]
    public async Task ListNamespaces_should_return_404_when_discovery_returns_null()
    {
        // given
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNamespaces(Arg.Any<CancellationToken>()).Returns(Task.FromResult<List<string>>(null!));
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage, discoveryProvider);

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ListNamespaces(context);

        // then
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ListServices_should_return_empty_when_no_discovery_provider()
    {
        // given
        _dataStorage.GetMonitoringApi().Returns(_monitoringApi);

        var context = _CreateHttpContext(_dataStorage);
        context.Request.RouteValues["namespace"] = "default";

        var options = new DashboardOptions();
        var builder = Substitute.For<IEndpointRouteBuilder>();
        builder.ServiceProvider.Returns(context.RequestServices);

        var provider = new RouteActionProvider(builder, options);

        // when
        await provider.ListServices(context);

        // then
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var response = await reader.ReadToEndAsync(AbortToken);
        response.Should().Be("[]");
    }

    private static DefaultHttpContext _CreateHttpContext(
        IDataStorage dataStorage,
        INodeDiscoveryProvider? discoveryProvider = null
    )
    {
        var services = new ServiceCollection().AddLogging().AddSingleton(dataStorage);

        if (discoveryProvider != null)
        {
            services.AddSingleton(discoveryProvider);
        }

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        return context;
    }
}
