// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Tests.Security;

public sealed class PingServicesSecurityTests : TestBase
{
    private static readonly Node _RegisteredNode = new()
    {
        Id = "node-id",
        Name = "allowed",
        Address = "allowed",
        Port = 8080,
        Tags = "messaging",
    };

    [Theory]
    [InlineData("http://allowed:8080")]
    [InlineData("http://allowed:8080/")]
    public async Task should_ping_exact_registered_origin(string endpoint)
    {
        // given
        using var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("OK"),
        });
        var factory = new StubHttpClientFactory(handler);
        await using var app = _CreateTestApp([_RegisteredNode], factory);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/ping?endpoint={Uri.EscapeDataString(endpoint)}", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Should().Be(new Uri("http://allowed:8080/messaging/api/health"));
        handler.CancellationTokens.Should().ContainSingle(token => token.CanBeCanceled);
        factory
            .RequestedNames.Should()
            .ContainSingle()
            .Which.Should()
            .Be(MessagingDashboardEndpoints.PingHttpClientName);
    }

    [Fact]
    public async Task should_accept_default_https_port_for_registered_origin()
    {
        // given
        var node = new Node
        {
            Id = "node-id",
            Name = "allowed",
            Address = "https://allowed",
            Port = 443,
            Tags = "messaging",
        };
        using var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("OK"),
        });
        var factory = new StubHttpClientFactory(handler);
        await using var app = _CreateTestApp([node], factory);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync(
            $"/api/ping?endpoint={Uri.EscapeDataString("https://allowed")}",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Requests.Should().ContainSingle().Which.Should().Be(new Uri("https://allowed/messaging/api/health"));
    }

    [Theory]
    [InlineData("http://allowed:8080@evil.test")]
    [InlineData("http://allowed.evil.test:8080")]
    [InlineData("http://allowed:8081")]
    [InlineData("https://allowed:8080")]
    [InlineData("http://allowed:8080/path")]
    [InlineData("http://allowed:8080/?query=value")]
    [InlineData("http://allowed:8080?")]
    [InlineData("http://allowed:8080/#fragment")]
    [InlineData("http://allowed:8080#")]
    [InlineData("ftp://allowed:8080")]
    [InlineData("not-a-uri")]
    public async Task should_reject_non_origin_or_mismatched_endpoint_without_request(string endpoint)
    {
        // given
        using var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("OK"),
        });
        var factory = new StubHttpClientFactory(handler);
        await using var app = _CreateTestApp([_RegisteredNode], factory);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync($"/api/ping?endpoint={Uri.EscapeDataString(endpoint)}", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Requests.Should().BeEmpty();
        factory.RequestedNames.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_follow_redirect_from_registered_origin()
    {
        // given
        using var handler = new CapturingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://evil.test/secrets");
            return response;
        });
        var factory = new StubHttpClientFactory(handler);
        await using var app = _CreateTestApp([_RegisteredNode], factory);
        await app.StartAsync(AbortToken);
        using var client = app.GetTestClient();

        // when
        var response = await client.GetAsync(
            $"/api/ping?endpoint={Uri.EscapeDataString("http://allowed:8080")}",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        handler
            .Requests.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new Uri("http://allowed:8080/messaging/api/health"));
        handler.Requests.Should().NotContain(uri => uri.Host == "evil.test");
    }

    [Fact]
    public void should_configure_ping_client_timeout_and_disable_redirects()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        new DashboardOptionsExtension(config => config.WithNoAuth()).AddServices(services);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var options = provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(MessagingDashboardEndpoints.PingHttpClientName);
        var handlerBuilder = new TestHttpMessageHandlerBuilder(provider)
        {
            Name = MessagingDashboardEndpoints.PingHttpClientName,
        };

        // when
        using var client = factory.CreateClient(MessagingDashboardEndpoints.PingHttpClientName);
        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(handlerBuilder);
        }

        // then
        client.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        handlerBuilder.PrimaryHandler.Should().BeOfType<HttpClientHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }

    private static WebApplication _CreateTestApp(IList<Node> nodes, IHttpClientFactory httpClientFactory)
    {
        var config = new MessagingDashboardOptionsBuilder().WithNoAuth();
        var discoveryProvider = Substitute.For<INodeDiscoveryProvider>();
        discoveryProvider.GetNodes(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(nodes));

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Auth);
        builder.Services.AddSingleton(Substitute.For<IAuthService>());
        builder.Services.AddSingleton(discoveryProvider);
        builder.Services.AddSingleton(httpClientFactory);
        builder.Services.AddSingleton(Substitute.For<IRequestMapper>());
        builder.Services.AddSingleton(new ConsulDiscoveryOptions { NodeName = "current-node" });
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<GatewayProxyAgent>();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddCors(options =>
            options.AddPolicy("HeadlessMessagingDashboardCORS", policy => policy.AllowAnyOrigin())
        );

        var app = builder.Build();
        app.UseRouting();
        app.UseCors("HeadlessMessagingDashboardCORS");
        app.UseAuthorization();
        app.MapMessagingDashboardEndpoints(config);

        return app;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TestHttpMessageHandlerBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];
        public override IServiceProvider Services { get; } = services;

        public override HttpMessageHandler Build()
        {
            return CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
        }
    }
}
