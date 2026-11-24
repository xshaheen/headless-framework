// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// The trace of one correlation id on node A, which reads its own store and asks node B: what each node adds, how the
/// call travels, and what the page is told when a node does not answer.
/// </summary>
public sealed class ForwardingTraceTests : TestBase
{
    private const string _CorrelationId = "order-7f3a";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly RecordingOperatorLog _log = new();

    public ForwardingTraceTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_name_node_b_unavailable_and_still_return_node_a_rows_when_node_b_times_out()
    {
        // given: node B is listed but never answers, and node A waits 200 ms for each store and node
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discoveryA,
            _time,
            _log,
            AbortToken,
            options => options.StoreTimeout = TimeSpan.FromMilliseconds(200)
        );
        _network.Hang("node-b");
        var local = await _PublishAsync(nodeA, "m-1");

        // when
        var trace = await _TraceAsync(nodeA);

        // then
        trace.Ids.Should().Equal($"node-a~primary~{local.StorageId:D}");
        trace.Unavailable.Should().Equal("Node node-b is not answering");
        trace.Limited.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_forward_a_forwarded_trace_again_and_send_no_correlation_id_in_the_url()
    {
        // given: node B lists node C, so a node B that forwarded again would call it
        var discoveryB = new StaticDiscovery();
        discoveryB.Set(ForwardingHost.Entry("node-c", "https://node-c"));
        await using var nodeC = await _StartPeerAsync("node-c", new StaticDiscovery());
        await using var nodeB = await _StartPeerAsync("node-b", discoveryB);
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discoveryA,
            _time,
            _log,
            AbortToken
        );
        var onB = await _PublishAsync(nodeB, "m-b");
        var onC = await _PublishAsync(nodeC, "m-c");

        // when
        var trace = await _TraceAsync(nodeA);

        // then
        trace.Ids.Should().Equal($"node-b~primary~{onB.StorageId:D}");
        trace.Ids.Should().NotContain($"node-c~primary~{onC.StorageId:D}");
        var traceCalls = _network.Calls.Where(call =>
            call.Uri.AbsolutePath.EndsWith("/peer/trace", StringComparison.Ordinal)
        );
        traceCalls
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<PeerNetwork.SentCall>(call =>
                call.Caller == "node-a" && call.Uri.Host == "node-b" && call.Method == HttpMethod.Post
            );
        _network.Calls.Should().NotContain(call => call.Caller == "node-b");
        _network
            .Calls.Should()
            .NotContain(call => call.Uri.OriginalString.Contains(_CorrelationId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_list_300_items_once_and_not_limited_when_two_nodes_share_one_store_of_300_rows()
    {
        // given: node A and node B read one store
        await using var nodeA = await ForwardingHost.StartAsync(
            "node-a",
            _network,
            _discoveryA,
            _time,
            _log,
            AbortToken
        );
        var shared = nodeA.Services.GetRequiredService<IDataStorage>();
        await using var nodeB = await _StartPeerAsync(
            "node-b",
            new StaticDiscovery(),
            services => services.AddSingleton(shared)
        );

        for (var i = 0; i < 300; i++)
        {
            await _PublishAsync(nodeA, "m-" + i.ToString(CultureInfo.InvariantCulture));
        }

        // when
        var trace = await _TraceAsync(nodeA);

        // then
        trace.Ids.Should().HaveCount(300).And.OnlyHaveUniqueItems();
        trace.Ids.Should().OnlyContain(id => id.StartsWith("node-a~", StringComparison.Ordinal));
        trace.Count.Should().Be(300);
        trace.Limited.Should().BeFalse();
        trace.Unavailable.Should().BeEmpty();
        _network
            .Calls.Should()
            .Contain(call => call.Uri.AbsolutePath.EndsWith("/peer/trace", StringComparison.Ordinal));
    }

    private async Task<TraceSection> _TraceAsync(WebApplication node)
    {
        using var client = MessagingCommandHost.Client(node);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);
        using var response = await MessagingCommandHost.PostAsync(
            client,
            "/headless/api/trace",
            new { correlationId = _CorrelationId },
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await MessagingCommandHost.ReadAsync(response, AbortToken);
        var messaging = json.GetProperty("modules")
            .EnumerateArray()
            .Single(module =>
                string.Equals(
                    module.GetProperty("id").GetString(),
                    MessagingDashboardOptions.ModuleId,
                    StringComparison.Ordinal
                )
            );
        messaging.GetProperty("stale").GetBoolean().Should().BeFalse();
        IReadOnlyList<string> unavailable = messaging.TryGetProperty("unavailable", out var names)
            ? [.. names.EnumerateArray().Select(static name => name.GetString()!)]
            : [];

        return new TraceSection(
            [
                .. json.GetProperty("rows")
                    .EnumerateArray()
                    .Select(row => row.GetProperty("target").GetProperty("id").GetString()!),
            ],
            unavailable,
            messaging.GetProperty("limited").GetBoolean(),
            messaging.GetProperty("count").GetInt32()
        );
    }

    // The Messaging module's section of a trace answer, and the ids of the rows it served.
    private sealed record TraceSection(List<string> Ids, IReadOnlyList<string> Unavailable, bool Limited, int Count);

    private async Task<MediumMessage> _PublishAsync(WebApplication node, string messageId)
    {
        var storage = node.Services.GetRequiredService<IDataStorage>();
        var message = MessagingRows.Medium(messageId);
        message.Origin.Headers[Headers.CorrelationId] = _CorrelationId;
        var stored = await storage.StoreMessageAsync(MessagingRows.Name, message, cancellationToken: AbortToken);
        (await storage.ChangePublishStateAsync(stored, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        return stored;
    }

    private async Task<WebApplication> _StartPeerAsync(
        string name,
        StaticDiscovery discovery,
        Action<IServiceCollection>? configure = null
    )
    {
        var app = await ForwardingHost.StartAsync(
            name,
            _network,
            discovery,
            _time,
            _log,
            AbortToken,
            configure: configure
        );
        _network.Add(name, app);

        return app;
    }
}
