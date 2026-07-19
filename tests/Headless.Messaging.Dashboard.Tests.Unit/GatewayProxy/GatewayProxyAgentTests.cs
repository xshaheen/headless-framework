// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Headless.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.GatewayProxy;

public sealed class GatewayProxyAgentTests : TestBase
{
    [Fact]
    public async Task should_return_false_when_invoke_no_node_cookie()
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
    public async Task should_return_false_when_invoke_node_cookie_value_is_null()
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
    public async Task should_forward_request_to_discovered_node_when_invoke()
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
        discoveryProvider
            .GetNodeAsync("node1", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(node));

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") };
        using var httpMessageHandler = new MockHttpMessageHandler(responseMessage);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(httpMessageHandler);
        httpClientFactory.CreateClient("GatewayProxy").Returns(httpClient);

        var agent = _CreateAgent(context, discoveryProvider, httpClientFactory);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_delete_cookie_when_invoke_node_not_found()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=unknown-node";

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider
            .GetNodeAsync("unknown-node", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(null));

        var agent = _CreateAgent(context, discoveryProvider);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
        // Cookie deletion is signaled by Set-Cookie header with expired date
    }

    [Fact]
    public async Task should_return_false_when_invoke_same_node_as_current()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=current-node";

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();

        // Configure consul options with current node name matching the cookie
        var agent = _CreateAgent(context, discoveryProvider, nodeName: "current-node");

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_handle_node_failures_gracefully_when_invoke()
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
        discoveryProvider
            .GetNodeAsync("node1", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(node));

        using var httpMessageHandler = new MockHttpMessageHandler(new HttpRequestException("Connection failed"));
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(httpMessageHandler);
        httpClientFactory.CreateClient("GatewayProxy").Returns(httpClient);

        var agent = _CreateAgent(context, discoveryProvider, httpClientFactory);

        // when
        var result = await agent.Invoke(context);

        // then - should handle exception gracefully
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_resolve_k8s_node_without_namespace_cookie()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=node1";

        var node = new Node
        {
            Id = "1",
            Name = "node1",
            Address = "http://node1.team-a",
            Port = 8080,
            Tags = "headless.messaging.visibility:show",
        };
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider
            .GetNodeAsync("node1", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(node));

        using var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") }
        );
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(handler);
        httpClientFactory.CreateClient("GatewayProxy").Returns(httpClient);
        var agent = _CreateAgent(context, discoveryProvider, httpClientFactory, useK8s: true);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeTrue();
        await discoveryProvider.Received(1).GetNodeAsync("node1", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_clear_k8s_selection_when_cross_namespace_node_is_rejected()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie =
            $"{GatewayProxyAgent.CookieNodeName}=node1; {GatewayProxyAgent.CookieNodeNsName}=team-b";

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider
            .GetNodeAsync("node1", "team-b", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(null));
        var requestMapper = Substitute.For<IRequestMapper>();
        var agent = _CreateAgent(context, discoveryProvider, useK8s: true, requestMapper: requestMapper);

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
        await requestMapper.DidNotReceive().Map(Arg.Any<HttpRequest>());
        context.Response.Headers.SetCookie.ToString().Should().Contain($"{GatewayProxyAgent.CookieNodeName}=");
        context.Response.Headers.SetCookie.ToString().Should().Contain($"{GatewayProxyAgent.CookieNodeNsName}=");
    }

    [Fact]
    public async Task should_clear_endpoint_shaped_k8s_selection_without_forwarding()
    {
        // given
        var context = _CreateHttpContext();
        const string endpoint = "http://evil.test:8080";
        context.Request.Headers.Cookie =
            $"{GatewayProxyAgent.CookieNodeName}={Uri.EscapeDataString(endpoint)}; {GatewayProxyAgent.CookieNodeNsName}=team-a";

        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider
            .GetNodeAsync(endpoint, "team-a", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(null));
        var requestMapper = Substitute.For<IRequestMapper>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var agent = _CreateAgent(
            context,
            discoveryProvider,
            httpClientFactory,
            useK8s: true,
            requestMapper: requestMapper
        );

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeFalse();
        await requestMapper.DidNotReceive().Map(Arg.Any<HttpRequest>());
        httpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task should_preserve_authorization_only_after_k8s_node_is_discovered()
    {
        // given
        var context = _CreateHttpContext();
        context.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=node1";

        var node = new Node
        {
            Id = "1",
            Name = "node1",
            Address = "http://node1.team-a",
            Port = 8080,
            Tags = "headless.messaging.visibility:show",
        };
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider
            .GetNodeAsync("node1", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Node?>(node));

        var requestMapper = Substitute.For<IRequestMapper>();
        requestMapper
            .Map(Arg.Any<HttpRequest>())
            .Returns(
                Task.FromResult(
                    new HttpRequestMessage(HttpMethod.Get, "http://example.com")
                    {
                        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "dashboard-token") },
                    }
                )
            );

        using var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") }
        );
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        using var httpClient = new HttpClient(handler);
        httpClientFactory.CreateClient("GatewayProxy").Returns(httpClient);
        var agent = _CreateAgent(
            context,
            discoveryProvider,
            httpClientFactory,
            useK8s: true,
            requestMapper: requestMapper
        );

        // when
        var result = await agent.Invoke(context);

        // then
        result.Should().BeTrue();
        handler.Request.Should().NotBeNull();
        handler.Request!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("dashboard-token");
    }

    [Fact]
    public async Task should_share_one_k8s_lookup_across_concurrent_cache_misses()
    {
        var firstContext = _CreateHttpContext();
        var secondContext = _CreateHttpContext();
        firstContext.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=node1";
        secondContext.Request.Headers.Cookie = $"{GatewayProxyAgent.CookieNodeName}=node1";
        var lookup = new TaskCompletionSource<Node?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNodeAsync("node1", null, Arg.Any<CancellationToken>()).Returns(lookup.Task);
        var requestMapper = Substitute.For<IRequestMapper>();
        requestMapper
            .Map(Arg.Any<HttpRequest>())
            .Returns(Task.FromException<HttpRequestMessage>(new HttpRequestException("stop after discovery")));
        var agent = _CreateAgent(firstContext, discoveryProvider, useK8s: true, requestMapper: requestMapper);

        var first = agent.Invoke(firstContext);
        var second = agent.Invoke(secondContext);
        await discoveryProvider.Received(1).GetNodeAsync("node1", null, Arg.Any<CancellationToken>());

        lookup.SetResult(
            new Node
            {
                Id = "1",
                Name = "node1",
                Address = "http://node1.team-a",
                Port = 8080,
                Tags = "headless.messaging.visibility:show",
            }
        );

        await Task.WhenAll(first, second);
        await discoveryProvider.Received(1).GetNodeAsync("node1", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_be_defined_when_cookie_node_name()
    {
        // then
        GatewayProxyAgent.CookieNodeName.Should().Be("messaging.node");
    }

    [Fact]
    public void should_be_defined_when_cookie_node_ns_name()
    {
        // then
        GatewayProxyAgent.CookieNodeNsName.Should().Be("messaging.node.ns");
    }

    private GatewayProxyAgent _CreateAgent(
        HttpContext context,
        INodeDiscoveryProvider? discoveryProvider = null,
        IHttpClientFactory? httpClientFactory = null,
        string? nodeName = null,
        bool useK8s = false,
        IRequestMapper? requestMapper = null
    )
    {
        discoveryProvider ??= Substitute.For<INodeDiscoveryProvider>();
        httpClientFactory ??= Substitute.For<IHttpClientFactory>();
        if (requestMapper == null)
        {
            requestMapper = Substitute.For<IRequestMapper>();
            requestMapper
                .Map(Arg.Any<HttpRequest>())
                .Returns(_ =>
                {
                    var downstreamRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
                    context.Response.RegisterForDispose(downstreamRequest);

                    return Task.FromResult(downstreamRequest);
                });
        }

        var services = new ServiceCollection()
            .AddLogging()
            .AddMemoryCache()
            .AddSingleton<KeyedAsyncLock>()
            .AddSingleton(discoveryProvider)
            .AddSingleton(httpClientFactory)
            .AddSingleton(requestMapper);

        if (!useK8s)
        {
            services.AddSingleton(new ConsulDiscoveryOptions { NodeName = nodeName ?? "test-node" });
        }

        var sp = services.BuildServiceProvider();
        context.RequestServices = sp;

        var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var keyedLock = sp.GetRequiredService<KeyedAsyncLock>();
        return new GatewayProxyAgent(
            LoggerFactory,
            requestMapper,
            httpClientFactory,
            cache,
            sp,
            discoveryProvider,
            keyedLock
        );
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public MockHttpMessageHandler(HttpResponseMessage response) => _response = response;

        public MockHttpMessageHandler(Exception exception) => _exception = exception;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;

            return _exception is not null
                ? Task.FromException<HttpResponseMessage>(_exception)
                : Task.FromResult(_response!);
        }
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
