// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// An id lookup on node A that reaches node B and node C: which nodes it asks, and when a node's last answer is used
/// instead of asking it again.
/// </summary>
public sealed class ForwardingResolveTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly RecordingOperatorLog _log = new();

    public ForwardingResolveTests()
    {
        _discoveryA.Set(
            ForwardingHost.Entry("node-b", "https://node-b"),
            ForwardingHost.Entry("node-c", "https://node-c")
        );
    }

    [Fact]
    public async Task should_ask_only_node_b_when_the_lookup_is_a_full_id_naming_node_b()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b", operate: false);
        await using var nodeC = await _StartPeerAsync("node-c", operate: false);
        await using var nodeA = await _StartAAsync();
        var row = await _FailureOnAsync(nodeB);
        var id = $"node-b~primary~{row.StorageId:D}";
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        var found = await _ResolveAsync(client, cookie, id);

        // then
        found.Should().Equal(id);
        _ResolveCalls("node-b").Should().Be(1);
        _ResolveCalls("node-c").Should().Be(0);
    }

    [Fact]
    public async Task should_ask_each_node_once_within_5_seconds_and_again_after_when_the_same_id_is_looked_up()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b", operate: false);
        await using var nodeC = await _StartPeerAsync("node-c", operate: false);
        await using var nodeA = await _StartAAsync();
        var row = await _FailureOnAsync(nodeB);
        var storageId = row.StorageId.ToString("D");
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        var first = await _ResolveAsync(client, cookie, storageId);
        _time.Advance(TimeSpan.FromSeconds(4));
        var second = await _ResolveAsync(client, cookie, storageId);
        var callsWithin5Seconds = (_ResolveCalls("node-b"), _ResolveCalls("node-c"));
        _time.Advance(TimeSpan.FromSeconds(1));
        var third = await _ResolveAsync(client, cookie, storageId);

        // then
        first.Should().Equal($"node-b~primary~{storageId}");
        second.Should().Equal(first);
        third.Should().Equal(first);
        callsWithin5Seconds.Should().Be((1, 1));
        _ResolveCalls("node-b").Should().Be(2);
        _ResolveCalls("node-c").Should().Be(2);
    }

    [Fact]
    public async Task should_ask_node_b_again_and_show_the_change_when_a_command_ran_on_node_b_since_the_last_lookup()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b", operate: true);
        await using var nodeC = await _StartPeerAsync("node-c", operate: true);
        await using var nodeA = await _StartAAsync();
        var row = await _FailureOnAsync(nodeB);
        var storageId = row.StorageId.ToString("D");
        var id = $"node-b~primary~{storageId}";
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        await _ResolveAsync(client, cookie, storageId);

        // when: node A purges the row on node B, then the same id is looked up within 5 seconds
        using var purged = await MessagingCommandHost.PostAsync(
            client,
            $"{MessagingCommandHost.Commands}/{MessagingCommands.PurgeId}",
            new { ids = new[] { id }, confirmed = true },
            cookie,
            AbortToken
        );
        var after = await _ResolveAsync(client, cookie, storageId);

        // then
        purged.StatusCode.Should().Be(HttpStatusCode.OK);
        after.Should().BeEmpty();
        _ResolveCalls("node-b").Should().Be(2);
        _ResolveCalls("node-c").Should().Be(1);
    }

    [Fact]
    public async Task should_ask_node_b_again_when_an_operation_chunk_ran_on_node_b_since_the_last_lookup()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b", operate: true);
        await using var nodeC = await _StartPeerAsync("node-c", operate: true);
        await using var nodeA = await _StartAAsync();
        var row = await _FailureOnAsync(nodeB);
        var storageId = row.StorageId.ToString("D");
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        await _ResolveAsync(client, cookie, storageId);
        var operations = $"{MessagingCommandHost.Commands}/{MessagingCommands.PurgeId}";
        using var confirm = await MessagingCommandHost.PostAsync(
            client,
            operations + "/confirm",
            new { filter = new { node = "node-b", status = "Failed" }, reason = "card processor recovered" },
            cookie,
            AbortToken
        );
        var token = (await MessagingCommandHost.ReadAsync(confirm, AbortToken)).GetProperty("token").GetString();

        // when: node A runs the Operation's one chunk on node B, then the same id is looked up within 5 seconds
        using var chunk = await MessagingCommandHost.PostAsync(
            client,
            operations + "/chunks",
            new { token, confirmed = true },
            cookie,
            AbortToken
        );
        var after = await _ResolveAsync(client, cookie, storageId);

        // then
        chunk.StatusCode.Should().Be(HttpStatusCode.OK);
        after.Should().BeEmpty();
        _ResolveCalls("node-b").Should().Be(2);
        _ResolveCalls("node-c").Should().Be(1);
    }

    [Fact]
    public async Task should_ask_node_b_again_when_node_a_relays_a_change_from_node_b_since_the_last_lookup()
    {
        // given: a stream on node A watches node B, and a lookup reached node B
        await using var nodeB = await _StartPeerAsync("node-b", operate: false);
        await using var nodeC = await _StartPeerAsync("node-c", operate: false);
        await using var nodeA = await _StartAAsync();
        var publisher = new RecordingEventPublisher();
        using var reader = ActivatorUtilities.CreateInstance<PeerChangeReader>(nodeA.Services, publisher);
        var started = _network.NextCallAsync(call =>
            string.Equals(call.Uri.Host, "node-b", StringComparison.Ordinal)
            && call.Uri.AbsolutePath.EndsWith("/peer/changes/start", StringComparison.Ordinal)
        );
        reader.Watch("node-b");
        await _AdvanceUntilAsync(started);
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);
        const string query = "order-7";
        var lookedUpAt = _time.GetUtcNow();
        var before = await _ResolveAsync(client, cookie, query);
        var relayed = publisher.NextAsync(e => string.Equals(e.Kind, "change", StringComparison.Ordinal));

        // when: a row arrives on node B and node A relays its change, then the same text is looked up again
        await MessagingCommandHost.InboxFailureAsync(
            nodeB.Services.GetRequiredService<IDataStorage>(),
            query,
            AbortToken
        );
        await _AdvanceUntilAsync(relayed);
        var after = await _ResolveAsync(client, cookie, query);

        // then: within the 5 seconds the answer would be reused, node B is asked again and shows the new row
        (_time.GetUtcNow() - lookedUpAt)
            .Should()
            .BeLessThan(MessagingForwarding.ResolveLifetime);
        before.Should().BeEmpty();
        after.Should().ContainSingle().Which.Should().StartWith("node-b~primary~");
        _ResolveCalls("node-b").Should().Be(2);
        _ResolveCalls("node-c").Should().Be(1);
    }

    [Fact]
    public async Task should_ask_no_node_when_the_lookup_is_a_full_id_naming_a_node_discovery_does_not_list()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b", operate: false);
        await using var nodeC = await _StartPeerAsync("node-c", operate: false);
        await using var nodeA = await _StartAAsync();
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        var found = await _ResolveAsync(client, cookie, $"node-x~primary~{Guid.NewGuid():D}");

        // then
        found.Should().BeEmpty();
        _ResolveCalls("node-b").Should().Be(0);
        _ResolveCalls("node-c").Should().Be(0);
    }

    [Fact]
    public async Task should_ask_node_c_again_when_it_did_not_answer_the_last_lookup()
    {
        // given: node C is listed but does not answer
        await using var nodeB = await _StartPeerAsync("node-b", operate: false);
        await using var nodeA = await _StartAAsync();
        var row = await _FailureOnAsync(nodeB);
        var storageId = row.StorageId.ToString("D");
        using var client = MessagingCommandHost.Client(nodeA);
        var cookie = await MessagingCommandHost.SignInAsync(client, "ops", AbortToken);

        // when
        var first = await _ResolveAsync(client, cookie, storageId);
        var second = await _ResolveAsync(client, cookie, storageId);

        // then
        first.Should().Equal($"node-b~primary~{storageId}");
        second.Should().Equal(first);
        _ResolveCalls("node-b").Should().Be(1);
        _ResolveCalls("node-c").Should().Be(2);
    }

    private int _ResolveCalls(string host) =>
        _network.Calls.Count(call =>
            string.Equals(call.Uri.Host, host, StringComparison.Ordinal)
            && call.Uri.AbsolutePath.EndsWith("/peer/resolve", StringComparison.Ordinal)
        );

    private async Task<List<string>> _ResolveAsync(HttpClient client, string cookie, string query)
    {
        using var response = await MessagingCommandHost.GetAsync(
            client,
            "/headless/api/resolve?q=" + Uri.EscapeDataString(query),
            cookie,
            AbortToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var messaging = (await MessagingCommandHost.ReadAsync(response, AbortToken))
            .GetProperty("modules")
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
                .Select(item => item.GetProperty("target").GetProperty("id").GetString()!),
        ];
    }

    private Task<Headless.Messaging.MediumMessage> _FailureOnAsync(WebApplication node) =>
        MessagingCommandHost.InboxFailureAsync(node.Services.GetRequiredService<IDataStorage>(), "order-1", AbortToken);

    private async Task<WebApplication> _StartPeerAsync(string name, bool operate)
    {
        var app = await ForwardingHost.StartAsync(
            name,
            _network,
            new StaticDiscovery(),
            _time,
            _log,
            AbortToken,
            options =>
            {
                if (operate)
                {
                    options.ForwardedAccess = MessagingForwardedAccess.Operate;
                }
            }
        );
        _network.Add(name, app);

        return app;
    }

    private Task<WebApplication> _StartAAsync() =>
        ForwardingHost.StartAsync("node-a", _network, _discoveryA, _time, _log, AbortToken);

    // Moves the shared clock in small steps, as real time would pass, until the task completes or 15 s of real time pass.
    private async Task _AdvanceUntilAsync(Task awaited)
    {
        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (!awaited.IsCompleted && DateTime.UtcNow < limit)
        {
            _time.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(10, AbortToken);
        }

        awaited.IsCompleted.Should().BeTrue("the reader should get there");
    }
}
