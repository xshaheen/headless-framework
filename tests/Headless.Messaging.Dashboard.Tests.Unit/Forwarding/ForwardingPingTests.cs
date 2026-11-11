// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// Ping: an operator asks this node to reach one discovered node, or every one, and reads one result per node. It
/// changes nothing, so it writes no operator-log entry, and it never reaches an address discovery did not return.
/// </summary>
public sealed class ForwardingPingTests : TestBase
{
    private const string _Ping = "/headless/api/messaging/nodes/ping";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly StaticDiscovery _discoveryB = new();
    private readonly RecordingOperatorLog _logA = new();
    private readonly RecordingOperatorLog _logB = new();

    public ForwardingPingTests()
    {
        _discoveryA.Set(
            ForwardingHost.Entry("node-b", "https://node-b"),
            ForwardingHost.Entry("node-c", "https://node-c")
        );
    }

    [Fact]
    public async Task should_answer_each_node_with_its_latency_and_the_one_out_of_time_with_timeout_when_pinging_all()
    {
        // given: node B answers; node C's call runs out of time, as HttpClient reports its own timeout
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        _network.Answer("node-c", _ => throw new TaskCanceledException("The request timed out."));
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(client, _Ping, new { }, cookie, AbortToken);

        // then: one result per discovered node, in discovery order, and nothing in either operator log
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("node").GetString().Should().Be("node-b");
        items[0].GetProperty("outcome").GetString().Should().Be("answered");
        items[0].GetProperty("latencyMs").GetDouble().Should().BeGreaterThanOrEqualTo(0);
        items[1].GetProperty("node").GetString().Should().Be("node-c");
        items[1].GetProperty("outcome").GetString().Should().Be("timeout");
        items[1].TryGetProperty("latencyMs", out _).Should().BeFalse();
        items[1].GetProperty("reason").GetString().Should().Be("Node node-c did not answer within 3 s");
        _network
            .Calls.Where(static call => call.Uri.AbsolutePath.EndsWith("/peer/ping", StringComparison.Ordinal))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(static call => call.Signed && call.Method == HttpMethod.Get);
        _logA.Entries.Should().BeEmpty();
        _logB.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_ping_only_the_named_node_when_the_body_names_one()
    {
        // given
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(
            client,
            _Ping,
            new { node = "node-b" },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await MessagingCommandHost.ReadAsync(response, AbortToken))
            .GetProperty("items")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Subject;
        item.GetProperty("node").GetString().Should().Be("node-b");
        item.GetProperty("outcome").GetString().Should().Be("answered");
        _network
            .Calls.Should()
            .ContainSingle()
            .Which.Uri.Should()
            .Be(new Uri("https://node-b/headless/api/messaging/peer/ping"));
    }

    [Fact]
    public async Task should_answer_failed_with_the_status_when_a_node_answers_the_ping_without_serving_it()
    {
        // given: node C answers, but has no ping endpoint, as a node that does not have it yet would
        await using var nodeA = await _StartAAsync();
        _network.Answer("node-c", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(
            client,
            _Ping,
            new { node = "node-c" },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("items")[0];
        item.GetProperty("outcome").GetString().Should().Be("failed");
        item.GetProperty("status").GetInt32().Should().Be(404);
        item.TryGetProperty("latencyMs", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_answer_failed_with_the_node_state_when_a_node_cannot_be_reached()
    {
        // given: discovery lists node B, but nothing listens at its address
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(
            client,
            _Ping,
            new { node = "node-b" },
            cookie,
            AbortToken
        );

        // then
        var item = (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("items")[0];
        item.GetProperty("outcome").GetString().Should().Be("failed");
        item.GetProperty("state").GetString().Should().Be("not-answering");
        item.GetProperty("reason").GetString().Should().Be("Node node-b is not answering");
    }

    [Fact]
    public async Task should_refuse_a_viewers_ping_with_403_and_call_no_node()
    {
        // given
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(client, _Ping, new { }, cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _network.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("node-x")]
    public async Task should_refuse_with_404_and_call_nothing_when_the_node_is_not_one_discovery_returned(string node)
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(client, _Ping, new { node }, cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_with_400_when_the_node_names_the_node_serving_the_page()
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.PostAsync(
            client,
            _Ping,
            new { node = "node-a" },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessagingCommandHost.ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("node")
            .GetString()
            .Should()
            .Be("invalid");
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_404_and_fan_out_to_no_node_when_another_node_forwards_a_ping()
    {
        // given: node B lets forwarded calls operate, and its discovery lists node C
        _discoveryB.Set(ForwardingHost.Entry("node-c", "https://node-c"));
        await using var nodeB = await _StartBAsync(static options =>
            options.ForwardedAccess = MessagingForwardedAccess.Operate
        );
        using var clientB = MessagingCommandHost.Client(nodeB);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_Ping, UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, "node-a");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.ActorHeader, "ops");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.AccessHeader, ForwardingProtocol.AccessOperate);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        await ForwardingHost.SignAsync(request, _time, AbortToken);

        // when
        using var response = await clientB.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_with_400_on_body_when_the_body_is_not_json()
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_Ping, UriKind.Relative))
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");

        // when
        using var response = await client.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MessagingCommandHost.ReadAsync(response, AbortToken))
            .GetProperty("rules")
            .GetProperty("body")
            .GetString()
            .Should()
            .Be("json");
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_no_results_for_all_and_404_for_a_named_node_when_forwarding_is_off()
    {
        // given: a node with no forwarding, so discovery lists no node it may call
        await using var node = await MessagingCommandHost.StartAsync(_time, _logA, AbortToken);
        using var client = MessagingCommandHost.Client(node);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var all = await MessagingCommandHost.PostAsync(client, _Ping, new { }, cookie, AbortToken);
        using var named = await MessagingCommandHost.PostAsync(
            client,
            _Ping,
            new { node = "node-b" },
            cookie,
            AbortToken
        );

        // then
        all.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await MessagingCommandHost.ReadAsync(all, AbortToken);
        result.GetProperty("items").GetArrayLength().Should().Be(0);
        result.TryGetProperty("discovery", out _).Should().BeFalse();
        named.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<WebApplication> _StartBAsync(Action<MessagingDashboardOptions>? options = null)
    {
        var app = await ForwardingHost.StartAsync("node-b", _network, _discoveryB, _time, _logB, AbortToken, options);
        _network.Add("node-b", app);

        return app;
    }

    private Task<WebApplication> _StartAAsync() =>
        ForwardingHost.StartAsync("node-a", _network, _discoveryA, _time, _logA, AbortToken);
}
