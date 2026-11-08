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
/// A stream on node A that watches one of node B's topics: B's changes reach it under B's name, B leaving discovery
/// marks only B stale, and a cursor B no longer knows restarts the feed and tells B's views to refetch.
/// </summary>
public sealed class PeerChangeReaderTests : TestBase
{
    private static readonly TimeSpan _Step = TimeSpan.FromMilliseconds(50);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly PeerNetwork _network = new();
    private readonly StaticDiscovery _discoveryA = new();
    private readonly RecordingOperatorLog _log = new();
    private readonly RecordingEventPublisher _publisher = new();

    public PeerChangeReaderTests()
    {
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
    }

    [Fact]
    public async Task should_relay_node_b_changes_under_node_b_when_a_stream_watches_a_node_b_topic()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b");
        await using var nodeA = await _StartAAsync();
        using var reader = _Reader(nodeA);
        var started = _network.NextCallAsync(call => _IsStart(call, "node-b"));
        reader.Watch("node-b");
        await _AdvanceUntilAsync(started);
        var change = _publisher.NextAsync(e =>
            string.Equals(e.Kind, "change", StringComparison.Ordinal)
            && string.Equals(e.Node, "node-b", StringComparison.Ordinal)
        );

        // when
        await MessagingCommandHost.InboxFailureAsync(
            nodeB.Services.GetRequiredService<IDataStorage>(),
            "order-1",
            AbortToken
        );
        await _AdvanceUntilAsync(change);

        // then
        var relayed = (await change).Change!;
        relayed.Target.Module.Should().Be(MessagingDashboardOptions.ModuleId);
        relayed.Target.Node.Should().Be("node-b");
        _publisher.Events.Should().NotContain(e => string.Equals(e.Kind, "stale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_mark_only_node_b_stale_within_5_seconds_when_node_b_leaves_discovery()
    {
        // given: node A watches node B and node C, and both answer
        _discoveryA.Set(
            ForwardingHost.Entry("node-b", "https://node-b"),
            ForwardingHost.Entry("node-c", "https://node-c")
        );
        await using var nodeB = await _StartPeerAsync("node-b");
        await using var nodeC = await _StartPeerAsync("node-c");
        await using var nodeA = await _StartAAsync();
        using var reader = _Reader(nodeA);
        var startedB = _network.NextCallAsync(call => _IsStart(call, "node-b"));
        var startedC = _network.NextCallAsync(call => _IsStart(call, "node-c"));
        reader.Watch("node-b");
        reader.Watch("node-c");
        await _AdvanceUntilAsync(Task.WhenAll(startedB, startedC));
        var stale = _publisher.NextAsync(e => string.Equals(e.Kind, "stale", StringComparison.Ordinal));

        // when
        var leftAt = _time.GetUtcNow();
        _discoveryA.Set(ForwardingHost.Entry("node-c", "https://node-c"));
        await _AdvanceUntilAsync(stale);

        // then
        (await stale)
            .Node.Should()
            .Be("node-b");
        (_time.GetUtcNow() - leftAt).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
        _publisher
            .Events.Should()
            .NotContain(e =>
                string.Equals(e.Kind, "stale", StringComparison.Ordinal)
                && !string.Equals(e.Node, "node-b", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_reset_node_b_views_when_node_b_returns_to_discovery_after_its_cursor_was_dropped()
    {
        // given: node A follows node B's feed, then node B leaves discovery and node A drops its cursor
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
        await using var nodeB = await _StartPeerAsync("node-b");
        await using var nodeA = await _StartAAsync();
        using var reader = _Reader(nodeA);
        var started = _network.NextCallAsync(call => _IsStart(call, "node-b"));
        reader.Watch("node-b");
        await _AdvanceUntilAsync(started);
        var stale = _publisher.NextAsync(e => string.Equals(e.Kind, "stale", StringComparison.Ordinal));
        _discoveryA.Set();
        await _AdvanceUntilAsync(stale);
        _publisher.Events.Should().NotContain(e => string.Equals(e.Kind, "reset", StringComparison.Ordinal));
        var reset = _publisher.NextAsync(e => string.Equals(e.Kind, "reset", StringComparison.Ordinal));
        var restarted = _network.NextCallAsync(call => _IsStart(call, "node-b"));

        // when: node B comes back, and its feed starts again at its latest change
        _discoveryA.Set(ForwardingHost.Entry("node-b", "https://node-b"));
        await _AdvanceUntilAsync(Task.WhenAll(reset, restarted));

        // then: node B's views refetch, because changes made while it was gone are not in the new feed
        (await reset)
            .Node.Should()
            .Be("node-b");
    }

    [Fact]
    public async Task should_restart_the_feed_and_reset_node_b_views_when_node_b_no_longer_knows_the_cursor()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b");
        await using var nodeA = await _StartAAsync();
        using var reader = _Reader(nodeA);
        var started = _network.NextCallAsync(call => _IsStart(call, "node-b"));
        reader.Watch("node-b");
        await _AdvanceUntilAsync(started);
        var reset = _publisher.NextAsync(e => string.Equals(e.Kind, "reset", StringComparison.Ordinal));
        var restarted = _network.NextCallAsync(call => _IsStart(call, "node-b"));

        // when: node B answers every read with a cursor it does not know, as after a restart
        _network.Answer(
            "node-b",
            request =>
                request.RequestUri!.AbsolutePath.EndsWith("/peer/changes", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.Gone)
                    : null
        );
        await _AdvanceUntilAsync(Task.WhenAll(reset, restarted));

        // then
        (await reset)
            .Node.Should()
            .Be("node-b");
        _publisher.Events.Should().NotContain(e => string.Equals(e.Kind, "stale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_stop_reading_node_b_when_its_last_watcher_leaves()
    {
        // given
        await using var nodeB = await _StartPeerAsync("node-b");
        await using var nodeA = await _StartAAsync();
        using var reader = _Reader(nodeA);
        reader.Watch("node-b");
        reader.Watch("node-b");

        // when
        reader.Unwatch("node-b");
        var stillWatched = reader.IsWatching("node-b");
        reader.Unwatch("node-b");

        // then
        stillWatched.Should().BeTrue();
        reader.IsWatching("node-b").Should().BeFalse();
    }

    private PeerChangeReader _Reader(WebApplication node) =>
        ActivatorUtilities.CreateInstance<PeerChangeReader>(node.Services, _publisher);

    private static bool _IsStart(PeerNetwork.SentCall call, string host) =>
        string.Equals(call.Uri.Host, host, StringComparison.Ordinal)
        && call.Uri.AbsolutePath.EndsWith("/peer/changes/start", StringComparison.Ordinal);

    private async Task<WebApplication> _StartPeerAsync(string name)
    {
        var app = await ForwardingHost.StartAsync(name, _network, new StaticDiscovery(), _time, _log, AbortToken);
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
            _time.Advance(_Step);
            await Task.Delay(10, AbortToken);
        }

        awaited.IsCompleted.Should().BeTrue("the reader should get there");
    }
}
