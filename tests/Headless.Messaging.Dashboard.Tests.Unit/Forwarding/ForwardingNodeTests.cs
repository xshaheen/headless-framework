// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net;
using Headless.Dashboard;
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
/// Which node answers a read, a Needs you, or an Operation that names another node, and what the serving node shows
/// when that node cannot answer.
/// </summary>
public sealed class ForwardingNodeTests : TestBase
{
    private const string _Api = "/headless/api/messaging";
    private const string _Commands = MessagingCommandHost.Commands;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly StaticDiscovery _discoveryB = new();
    private readonly RecordingOperatorLog _logA = new();
    private readonly RecordingOperatorLog _logB = new();

    public ForwardingNodeTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_refuse_with_404_and_call_no_node_when_the_node_parameter_names_no_discovered_node()
    {
        // given
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/received?node=node-x",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _DetailAsync(response)).Should().Be("Discovery lists no Messaging node named 'node-x'.");
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_relay_the_read_to_node_b_and_show_its_rows_when_the_node_parameter_names_node_b()
    {
        // given
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        var row = await MessagingCommandHost.InboxFailureAsync(
            nodeB.Services.GetRequiredService<IDataStorage>(),
            "order-1",
            AbortToken
        );
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/received?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain(row.StorageId.ToString("D"));
        var call = _network.Calls.Should().ContainSingle().Subject;
        call.Uri.Should().Be(new Uri("https://node-b/headless/api/messaging/received"));
        call.Access.Should().Be(ForwardingProtocol.AccessView);
        call.Actor.Should().Be("ops");
    }

    [Fact]
    public async Task should_answer_502_with_the_node_state_and_none_of_its_own_rows_when_node_b_does_not_answer()
    {
        // given: discovery lists node B, but nothing answers at its address
        await using var nodeA = await _StartAAsync();
        var local = await MessagingCommandHost.InboxFailureAsync(
            nodeA.Services.GetRequiredService<IDataStorage>(),
            "order-1",
            AbortToken
        );
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await MessagingCommandHost.GetAsync(
            client,
            $"{_Api}/received?node=node-b",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Headers.GetValues(ForwardingProtocol.NodeStateHeader).Should().Equal(PeerState.NotAnswering.ToWire());
        var body = await response.Content.ReadAsStringAsync(AbortToken);
        body.Should().Contain("Node node-b is not answering").And.NotContain(local.StorageId.ToString("D"));
    }

    [Fact]
    public async Task should_serve_a_forwarded_read_here_and_forward_it_no_further_when_it_names_another_node()
    {
        // given: node B's discovery lists node C, and node A's forwarded call names it
        _discoveryB.Set(ForwardingHost.Entry("node-c", "https://node-c"));
        await using var nodeB = await _StartBAsync();
        using var clientB = MessagingCommandHost.Client(nodeB);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{_Api}/received?node=node-c", UriKind.Relative)
        );
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, "node-a");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.KeyHeader, ForwardingHost.Key);
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.ActorHeader, "ops");
        request.Headers.TryAddWithoutValidation(ForwardingProtocol.AccessHeader, ForwardingProtocol.AccessView);

        // when
        using var response = await clientB.SendAsync(request, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _network.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fold_two_instances_of_one_node_into_one_needs_you_item_when_neither_answers()
    {
        // given: discovery lists two instances of node B, neither of which answers
        _discoveryA.Set(
            ForwardingHost.Entry("node-b", "https://node-b-1", id: "node-b-1"),
            ForwardingHost.Entry("node-b", "https://node-b-2", id: "node-b-2")
        );
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        var items = await _NeedsYouAsync(client, cookie);

        // then
        items
            .Where(item => item.Id.StartsWith("node-state:", StringComparison.Ordinal))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(("node-state:node-b", "Node node-b is not answering"));
        _network.Calls.Select(call => call.Uri.Host).Distinct().Should().BeEquivalentTo("node-b-1", "node-b-2");
    }

    [Fact]
    public async Task should_list_both_nodes_needs_you_labelled_and_ask_node_b_once_in_30_seconds()
    {
        // given
        await using var nodeB = await _StartBAsync();
        await using var nodeA = await _StartAAsync();
        await MessagingCommandHost.InboxFailureAsync(
            nodeA.Services.GetRequiredService<IDataStorage>(),
            "order-a",
            AbortToken
        );
        await MessagingCommandHost.InboxFailureAsync(
            nodeB.Services.GetRequiredService<IDataStorage>(),
            "order-b",
            AbortToken
        );
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        var first = await _NeedsYouAsync(client, cookie);
        var second = await _NeedsYouAsync(client, cookie);
        var askedWithin30Seconds = _AttentionCalls();
        _time.Advance(TimeSpan.FromSeconds(31));
        await _NeedsYouAsync(client, cookie);

        // then
        first.Should().Contain(item => item.Title.EndsWith(" (node node-a)", StringComparison.Ordinal));
        first
            .Should()
            .Contain(item =>
                item.Title.EndsWith(" (node node-b)", StringComparison.Ordinal)
                && item.Id.StartsWith("node-b:", StringComparison.Ordinal)
            );
        second.Should().BeEquivalentTo(first);
        askedWithin30Seconds.Should().Be(1);
        _AttentionCalls().Should().Be(2);
    }

    [Fact]
    public async Task should_run_every_chunk_on_node_b_when_an_operation_selects_rows_on_node_b()
    {
        // given
        const int rowCount = 3;
        await using var nodeB = await _StartBAsync(options =>
            options.ForwardedAccess = MessagingForwardedAccess.Operate
        );
        await using var nodeA = await _StartAAsync();
        var storageA = nodeA.Services.GetRequiredService<IDataStorage>();
        var storageB = nodeB.Services.GetRequiredService<IDataStorage>();

        for (var i = 0; i < rowCount; i++)
        {
            await MessagingCommandHost.InboxFailureAsync(
                storageB,
                "order-" + i.ToString(CultureInfo.InvariantCulture),
                AbortToken
            );
        }

        await MessagingCommandHost.InboxFailureAsync(storageA, "order-local", AbortToken);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var confirm = await MessagingCommandHost.PostAsync(
            client,
            $"{_Commands}/{MessagingCommands.ReExecuteId}/confirm",
            new { filter = new { node = "node-b", status = "Failed" }, reason = "card processor recovered" },
            cookie,
            AbortToken
        );
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var scope = await MessagingCommandHost.ReadAsync(confirm, AbortToken);
        scope.GetProperty("count").GetInt64().Should().Be(rowCount);
        var token = scope.GetProperty("token").GetString()!;
        var done = 0;
        string? cursor = null;

        do
        {
            using var chunk = await MessagingCommandHost.PostAsync(
                client,
                $"{_Commands}/{MessagingCommands.ReExecuteId}/chunks",
                new
                {
                    token,
                    cursor,
                    confirmed = true,
                    typedCount = rowCount,
                },
                cookie,
                AbortToken
            );
            chunk.StatusCode.Should().Be(HttpStatusCode.OK);
            var answer = await MessagingCommandHost.ReadAsync(chunk, AbortToken);
            done += answer
                .GetProperty("rows")
                .EnumerateArray()
                .Count(row => string.Equals(row.GetProperty("outcome").GetString(), "done", StringComparison.Ordinal));
            // The last chunk answers done and carries no cursor.
            cursor = answer.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
        } while (cursor is not null);

        // then
        done.Should().Be(rowCount);
        var chunks = _logB.Entries.Where(entry => entry.Action == OperatorLogAction.OperationChunk).ToList();
        chunks.Should().NotBeEmpty().And.OnlyContain(entry => entry.Actor == "ops via node-a");
        chunks.Sum(entry => entry.Done).Should().Be(rowCount);
        // Node A's shell records the chunks its operator ran; every row they name is node B's.
        _logA
            .Entries.Where(entry => entry.Action == OperatorLogAction.OperationChunk)
            .SelectMany(entry => entry.Ids)
            .Should()
            .HaveCount(rowCount)
            .And.OnlyContain(id => id.StartsWith("node-b~", StringComparison.Ordinal));
        (await _ReceivedCountAsync(storageB)).Should().Be(2 * rowCount);
        (await _ReceivedCountAsync(storageA)).Should().Be(1);
    }

    private async Task<WebApplication> _StartBAsync(Action<MessagingDashboardOptions>? options = null)
    {
        var app = await ForwardingHost.StartAsync("node-b", _network, _discoveryB, _time, _logB, AbortToken, options);
        _network.Add("node-b", app);

        return app;
    }

    private Task<WebApplication> _StartAAsync() =>
        ForwardingHost.StartAsync("node-a", _network, _discoveryA, _time, _logA, AbortToken);

    private int _AttentionCalls() =>
        _network.Calls.Count(call => call.Uri.AbsolutePath.EndsWith("/peer/attention", StringComparison.Ordinal));

    private async Task<List<(string Id, string Title)>> _NeedsYouAsync(HttpClient client, string cookie)
    {
        using var response = await MessagingCommandHost.GetAsync(client, "/headless/api/needs-you", cookie, AbortToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var modules = (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("modules");
        var messaging = modules
            .EnumerateArray()
            .Single(module =>
                string.Equals(
                    module.GetProperty("id").GetString(),
                    MessagingDashboardOptions.ModuleId,
                    StringComparison.Ordinal
                )
            );

        return
        [
            .. messaging
                .GetProperty("items")
                .EnumerateArray()
                .Select(item => (item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!)),
        ];
    }

    private async Task<string?> _DetailAsync(HttpResponseMessage response) =>
        (await MessagingCommandHost.ReadAsync(response, AbortToken)).GetProperty("detail").GetString();

    private async Task<long> _ReceivedCountAsync(IDataStorage storage) =>
        (
            await storage
                .GetMonitoringApi()
                .CountMessagesAsync(new MessageListQuery { MessageType = MessageType.Subscribe }, 100, AbortToken)
        ).Count;
}
