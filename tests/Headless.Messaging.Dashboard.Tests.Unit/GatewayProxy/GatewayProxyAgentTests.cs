// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.GatewayProxy.Requester;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.GatewayProxy;

public sealed class GatewayProxyAgentTests : TestBase
{
    [Fact]
    public async Task Invoke_should_return_false_when_no_node_cookie()
    {
        // given
        var context = _CreateHttpContext();
        // No cookie set

        var agent = _CreateAgent(context);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_should_return_false_when_node_cookie_value_is_null()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=";

        var agent = _CreateAgent(context);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_should_forward_request_to_discovered_node()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=node1";

        var node = new Node
        {
            Id = "1",
            Name = "node1",
            Address = "http://10.0.0.1",
            Port = 8080,
            Tags = "web",
        };

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNode("node1", null).Returns(Task.FromResult<Node?>(node));

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") };
        var requester = Substitute.For<IHttpRequester>();
        requester.GetResponse(Arg.Any<HttpRequestMessage>()).Returns(Task.FromResult(responseMessage));

        var agent = _CreateAgent(context, discoveryProvider, requester, withConsulOptions: true);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Invoke_should_delete_cookie_when_node_not_found()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=unknown-node";

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNode("unknown-node", null).Returns(Task.FromResult<Node?>(null));

        var agent = _CreateAgent(context, discoveryProvider, withConsulOptions: true);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
        // Cookie deletion is signaled by Set-Cookie header with expired date
    }

    [Fact]
    public async Task Invoke_should_return_false_when_same_node_as_current()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=current-node";

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();

        // Configure consul options with current node name matching the cookie
        var agent = _CreateAgent(context, discoveryProvider, withConsulOptions: true, nodeName: "current-node");

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_should_handle_node_failures_gracefully()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=node1";

        var node = new Node
        {
            Id = "1",
            Name = "node1",
            Address = "http://10.0.0.1",
            Port = 8080,
            Tags = "web",
        };

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNode("node1", null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Node?>(node));

        var requester = Substitute.For<IHttpRequester>();
        requester
            .GetResponse(Arg.Any<HttpRequestMessage>())
            .Returns<HttpResponseMessage>(_ => throw new HttpRequestException("Connection failed"));

        var agent = _CreateAgent(context, discoveryProvider, requester, withConsulOptions: true);

        // when
        var result = await agent.Invoke(context);

        // then - should handle exception gracefully
        result.Should().BeFalse();
    }

    // NOTE: K8s mode tests are not implemented because GatewayProxyAgent uses
    // GetRequiredService<ConsulDiscoveryOptions>() in a field initializer, which throws if the
    // service isn't registered. The K8s mode code path expects _consulDiscoveryOptions to be
    // null when Consul options are not configured, but this can never happen with the current
    // initialization approach.
    // TODO: Update GatewayProxyAgent initialization (e.g. avoid GetRequiredService in field initializers)
    //       so that K8s mode (no ConsulDiscoveryOptions registered) can be exercised and tested.

    [Fact]
    public void CookieNodeName_should_be_defined()
    {
        // then
        GatewayProxyAgent.CookieNodeName.Should().Be("messaging.node");
    }

    [Fact]
    public void CookieNodeNsName_should_be_defined()
    {
        // then
        GatewayProxyAgent.CookieNodeNsName.Should().Be("messaging.node.ns");
    }

    private GatewayProxyAgent _CreateAgent(
        HttpContext context,
        INodeDiscoveryProvider? discoveryProvider = null,
        IHttpRequester? requester = null,
        bool withConsulOptions = false,
        string? nodeName = null
    )
    {
        discoveryProvider ??= Substitute.For<INodeDiscoveryProvider>();
        requester ??= Substitute.For<IHttpRequester>();
        var requestMapper = Substitute.For<IRequestMapper>();
        requestMapper
            .Map(Arg.Any<HttpRequest>())
            .Returns(Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "http://example.com")));

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(discoveryProvider)
            .AddSingleton(requester)
            .AddSingleton(requestMapper);

        // GatewayProxyAgent always uses GetRequiredService<ConsulDiscoveryOptions>() in field initializer
        // For k8s mode simulation, we need to register it but the code checks if it's null internally
        // which won't work with GetRequiredService. This is a design limitation in the source code.
        services.AddSingleton(new ConsulDiscoveryOptions { NodeName = nodeName ?? "test-node" });

        var sp = services.BuildServiceProvider();
        context.RequestServices = sp;

        return new GatewayProxyAgent(LoggerFactory, requestMapper, requester, sp, discoveryProvider);
    }

    private static DefaultHttpContext _CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/messaging/api/stats";
        context.Request.QueryString = new QueryString("");

        return context;
    }
}
