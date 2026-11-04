// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Consul;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Dashboard.Consul;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class ConsulNodeDiscoveryProviderTests : TestBase
{
    [Fact]
    public async Task should_preserve_caller_cancellation_when_getting_node()
    {
        using var cache = new MessagingDashboardCache();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        using var provider = _CreateProvider(cache);

        Func<Task> act = async () =>
            _ = await provider.GetNodeAsync("test-node", cancellationToken: cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_preserve_caller_cancellation_when_getting_nodes()
    {
        using var cache = new MessagingDashboardCache();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        using var provider = _CreateProvider(cache);

        Func<Task> act = async () => _ = await provider.GetNodesAsync(cancellationToken: cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_preserve_caller_cancellation_when_registering_node()
    {
        using var cache = new MessagingDashboardCache();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        using var provider = _CreateProvider(cache);

        var act = () => provider.RegisterNodeAsync(cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_query_only_messaging_tagged_services_when_getting_nodes()
    {
        // given
        using var cache = new MessagingDashboardCache();
        using var handler = new CatalogHandler();
        using var consul = _CreateConsulClient(handler, out var httpClient);
        using var http = httpClient;
        using var provider = _CreateProvider(cache, new ConsulDiscoveryOptions { NodeName = "test-node" }, consul);

        // when
        var nodes = await provider.GetNodesAsync(cancellationToken: AbortToken);

        // then
        nodes.Should().ContainSingle();
        nodes[0].Name.Should().Be("orders");
        nodes[0].Address.Should().Be("http://10.0.0.5");
        nodes[0].Port.Should().Be(8080);
        handler.RequestedPaths.Should().BeEquivalentTo(["/v1/catalog/services", "/v1/catalog/service/orders"]);
        cache.TryGetValue<int>("messaging.nodes.count", out var count).Should().BeTrue();
        count.Should().Be(1);
    }

    [Fact]
    public async Task should_reuse_one_client_when_getting_nodes_repeatedly()
    {
        // given
        using var cache = new MessagingDashboardCache();
        using var handler = new CatalogHandler();
        using var consul = _CreateConsulClient(handler, out var httpClient);
        using var http = httpClient;
        using var provider = _CreateProvider(cache, new ConsulDiscoveryOptions { NodeName = "test-node" }, consul);

        // when
        await provider.GetNodesAsync(cancellationToken: AbortToken);
        await provider.GetNodesAsync(cancellationToken: AbortToken);

        // then: both calls went through the one injected client
        handler.RequestedPaths.Should().HaveCount(4);
    }

    [Fact]
    public async Task should_report_no_nodes_and_cache_zero_when_catalog_listing_fails()
    {
        // given
        using var cache = new MessagingDashboardCache();
        using var handler = new CatalogHandler { Status = HttpStatusCode.InternalServerError };
        using var consul = _CreateConsulClient(handler, out var httpClient);
        using var http = httpClient;
        using var provider = _CreateProvider(cache, new ConsulDiscoveryOptions { NodeName = "test-node" }, consul);

        // when
        var nodes = await provider.GetNodesAsync(cancellationToken: AbortToken);

        // then
        nodes.Should().BeEmpty();
        cache.TryGetValue<int>("messaging.nodes.count", out var count).Should().BeTrue();
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_register_tcp_check_and_custom_tags_when_scheme_is_https()
    {
        // given
        using var cache = new MessagingDashboardCache();
        using var handler = new CatalogHandler();
        using var consul = _CreateConsulClient(handler, out var httpClient);
        using var http = httpClient;
        var options = new ConsulDiscoveryOptions
        {
            NodeName = "test-node",
            NodeId = "node-1",
            Scheme = "https",
            CurrentNodeHostName = "node.internal",
            CurrentNodePort = 7443,
            CustomTags = ["blue", "Messaging"],
        };
        using var provider = _CreateProvider(cache, options, consul);

        // when
        await provider.RegisterNodeAsync(AbortToken);

        // then
        handler.RequestedPaths.Should().Equal("/v1/agent/service/register");
        handler.LastBody.Should().Contain("\"TCP\":\"node.internal:7443\"").And.NotContain("\"HTTP\"");
        handler.LastBody.Should().Contain("\"blue\"");
    }

    [Fact]
    public async Task should_register_http_check_when_scheme_is_http()
    {
        // given
        using var cache = new MessagingDashboardCache();
        using var handler = new CatalogHandler();
        using var consul = _CreateConsulClient(handler, out var httpClient);
        using var http = httpClient;
        var options = new ConsulDiscoveryOptions
        {
            NodeName = "test-node",
            CurrentNodeHostName = "node.internal",
            CurrentNodePort = 5000,
        };
        using var provider = _CreateProvider(cache, options, consul);

        // when
        await provider.RegisterNodeAsync(AbortToken);

        // then
        handler.LastBody.Should().Contain("http://node.internal:5000/messaging/dashboard/api/health");
    }

    private static ConsulNodeDiscoveryProvider _CreateProvider(MessagingDashboardCache cache)
    {
        return new(
            NullLoggerFactory.Instance,
            cache,
            new ConsulDiscoveryOptions
            {
                DiscoveryServerHostName = "127.0.0.1",
                DiscoveryServerPort = 1,
                NodeName = "test-node",
            }
        );
    }

    private static ConsulNodeDiscoveryProvider _CreateProvider(
        MessagingDashboardCache cache,
        ConsulDiscoveryOptions options,
        ConsulClient consul
    )
    {
        return new(NullLoggerFactory.Instance, cache, options, consul);
    }

    private static ConsulClient _CreateConsulClient(CatalogHandler handler, out HttpClient httpClient)
    {
        httpClient = new HttpClient(handler, disposeHandler: false);
        // ConsulClient refuses an HttpClient that does not accept JSON.
        httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json")
        );
        var configuration = new ConsulClientConfiguration { Address = new Uri("http://consul.test:8500") };
        return new ConsulClient(configuration, httpClient);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            RequestedPaths.Add(path);
            LastBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // "Messaging" in mixed case is how dashboard nodes register the tag; the other services carry no such tag.
            var json = path switch
            {
                "/v1/catalog/services" => """{"consul":[],"web":["http","edge"],"orders":["Headless","Messaging"]}""",
                "/v1/catalog/service/orders" => """
                    [{"Node":"n1","Address":"10.0.0.5","ServiceID":"orders-1","ServiceName":"orders","ServiceAddress":"10.0.0.5","ServicePort":8080,"ServiceTags":["Headless","Messaging"]}]
                    """,
                "/v1/agent/service/register" => "",
                _ => "[]",
            };

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
