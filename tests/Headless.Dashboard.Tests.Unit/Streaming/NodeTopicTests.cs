// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Streaming;

/// <summary>
/// Node topics, <c>{module}.{kind}@{node}</c>: exact delivery of one node's changes, the watch and unwatch calls the
/// owning module gets, node-scoped stale and reset events, and a change read that reaches only some sources.
/// </summary>
public sealed class NodeTopicTests : TestBase
{
    private static readonly TimeSpan _Coalesce = DashboardEventPublisher.CoalesceWindow;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeChangeStore _store;

    public NodeTopicTests()
    {
        _store = new FakeChangeStore(_time);
    }

    [Fact]
    public async Task should_send_a_node_topic_stream_only_the_changes_of_its_own_node()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.received@node-b"),
            AbortToken
        );
        var subscribed = await stream.NextAsync(AbortToken);
        _Strings(subscribed!.Json.GetProperty("topics")).Should().Equal("diag.received@node-b");

        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();
        publisher.Publish(_Change("node-c", "m-1"));
        publisher.Publish(FakeChangeStore.Change("diag", "received", "m-2", "1"));
        publisher.Publish(_Change("node-b", "m-3"));
        _time.Advance(_Coalesce);

        var change = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        change.Type.Should().Be("change");
        change.Json.GetProperty("id").GetString().Should().Be("m-3");
        change.Json.GetProperty("node").GetString().Should().Be("node-b");
        change.Json.GetProperty("topic").GetString().Should().Be("diag.received@node-b");
    }

    [Fact]
    public async Task should_keep_a_node_change_off_a_stream_subscribed_to_the_topic_without_a_node()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.received"),
            AbortToken
        );
        await stream.NextAsync(AbortToken);

        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();
        publisher.Publish(_Change("node-b", "m-1"));
        publisher.Publish(FakeChangeStore.Change("diag", "received", "m-2", "1"));
        _time.Advance(_Coalesce);

        var change = await FakeClock.WithinAsync(stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        change.Json.GetProperty("id").GetString().Should().Be("m-2");
        change.Json.TryGetProperty("node", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_tell_the_module_when_the_first_stream_watches_a_node_topic_and_when_the_last_leaves()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();
        var module = app.Services.GetRequiredService<NodeDiagModule>();
        var nodeB = StreamTestHost.StreamPath("diag.received@node-b");

        await using (var first = await SseStream.OpenAsync(client, nodeB, AbortToken))
        await using (var second = await SseStream.OpenAsync(client, nodeB, AbortToken))
        // A plain topic of the same module is no node topic.
        await using (var plain = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken))
        {
            await first.NextAsync(AbortToken);
            await second.NextAsync(AbortToken);
            await plain.NextAsync(AbortToken);

            module.Calls.Should().Equal("watch diag.received@node-b");
            await FakeClock.WithinAsync(_store.Started(1), AbortToken);
        }

        // The readers stop only once every stream's lease is released, and each lease uncounts its topics first, so
        // once they stop every call the closes cause has been made.
        await FakeClock.WithinAsync(_store.Stopped(1), AbortToken);

        module.Calls.Should().Equal("watch diag.received@node-b", "unwatch diag.received@node-b");
    }

    [Fact]
    public async Task should_mark_only_the_streams_watching_a_node_stale_and_reset_them()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();
        await using var nodeB = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.received@node-b"),
            AbortToken
        );
        await using var nodeC = await SseStream.OpenAsync(
            client,
            StreamTestHost.StreamPath("diag.received@node-c"),
            AbortToken
        );
        await nodeB.NextAsync(AbortToken);
        await nodeC.NextAsync(AbortToken);

        var publisher = app.Services.GetRequiredService<IDashboardEventPublisher>();
        publisher.PublishNodeStale("diag", "node-b", stale: true);
        publisher.PublishNodeReset("diag", "node-b");

        // node-c's stream hears only its own change, which proves node-b's events passed it by.
        publisher.Publish(_Change("node-c", "c-1"));
        _time.Advance(_Coalesce);

        var stale = await FakeClock.WithinAsync(nodeB.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var reset = await FakeClock.WithinAsync(nodeB.NextExceptHeartbeatAsync(AbortToken), AbortToken);
        var otherNode = await FakeClock.WithinAsync(nodeC.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        stale.Type.Should().Be("node.stale");
        stale.Json.GetProperty("module").GetString().Should().Be("diag");
        stale.Json.GetProperty("node").GetString().Should().Be("node-b");
        stale.Json.GetProperty("stale").GetBoolean().Should().BeTrue();
        reset.Type.Should().Be("node.reset");
        reset.Json.GetProperty("node").GetString().Should().Be("node-b");
        otherNode.Type.Should().Be("change");
        otherNode.Json.GetProperty("id").GetString().Should().Be("c-1");
    }

    [Fact]
    public async Task should_tell_a_stream_that_opens_later_that_its_node_is_stale()
    {
        await using var app = await _StartAsync();
        using var client = app.GetTestClient();
        var path = StreamTestHost.StreamPath("diag.received@node-b");
        await using var keeper = await SseStream.OpenAsync(client, path, AbortToken);
        await keeper.NextAsync(AbortToken);

        app.Services.GetRequiredService<IDashboardEventPublisher>().PublishNodeStale("diag", "node-b", stale: true);

        await using var later = await SseStream.OpenAsync(client, path, AbortToken);
        await later.NextAsync(AbortToken);
        var stale = await FakeClock.WithinAsync(later.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        stale.Type.Should().Be("node.stale");
        stale.Json.GetProperty("node").GetString().Should().Be("node-b");
    }

    [Fact]
    public async Task should_name_the_sources_a_change_read_missed_while_the_module_stays_live()
    {
        _store.UnavailableSources = ["outbox-2"];

        await using var app = await _StartAsync();
        using var client = app.GetTestClient();
        await using var stream = await SseStream.OpenAsync(client, StreamTestHost.StreamPath("diag.run"), AbortToken);
        await stream.NextAsync(AbortToken);

        var partial = await FakeClock.AdvanceUntilAsync(_time, stream.NextExceptHeartbeatAsync(AbortToken), AbortToken);

        partial.Type.Should().Be("module.stale");
        partial.Json.GetProperty("stale").GetBoolean().Should().BeFalse();
        _Strings(partial.Json.GetProperty("unavailable")).Should().Equal("outbox-2");

        _store.UnavailableSources = [];
        var recovered = await FakeClock.AdvanceUntilAsync(
            _time,
            stream.NextExceptHeartbeatAsync(AbortToken),
            AbortToken
        );

        recovered.Type.Should().Be("module.stale");
        recovered.Json.GetProperty("stale").GetBoolean().Should().BeFalse();
        recovered.Json.TryGetProperty("unavailable", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("diag.received@node-b", "diag", "received", "node-b")]
    [InlineData("messaging.received@node-b", "messaging", "received", "node-b")]
    public void should_split_a_node_topic_into_its_module_kind_and_node(
        string topic,
        string module,
        string kind,
        string node
    )
    {
        var parsed = ChangeReaderCoordinator.ParseNodeTopic(topic);

        parsed
            .Should()
            .BeEquivalentTo(
                new
                {
                    Module = module,
                    Kind = kind,
                    Node = node,
                    Topic = topic,
                }
            );
    }

    [Theory]
    [InlineData("diag.received")]
    [InlineData("diag.@node-b")]
    [InlineData("diag.received@")]
    [InlineData("diag@node-b")]
    public void should_read_no_node_from_a_topic_without_one(string topic)
    {
        ChangeReaderCoordinator.ParseNodeTopic(topic).Should().BeNull();
    }

    [Fact]
    public void should_name_the_node_in_an_object_topic_only_when_the_object_has_one()
    {
        var shared = new ObjectRef
        {
            Module = "messaging",
            Kind = "received",
            Id = "m-1",
        };

        StreamConnection.Topic(shared).Should().Be("messaging.received");
        StreamConnection.Topic(shared with { Node = "node-b" }).Should().Be("messaging.received@node-b");
    }

    private Task<WebApplication> _StartAsync()
    {
        return StreamTestHost.StartAsync(
            _time,
            _store,
            dashboard => dashboard.AddModule<NodeDiagModule, DiagStreamModuleOptions>("diag"),
            services: null,
            AbortToken
        );
    }

    private static DashboardChange _Change(string node, string id)
    {
        var change = FakeChangeStore.Change("diag", "received", id, "1");

        return change with
        {
            Target = change.Target with { Node = node },
        };
    }

    private static IEnumerable<string> _Strings(JsonElement array) =>
        array.EnumerateArray().Select(static item => item.GetString()!);
}

/// <summary>The stream test module, recording each watch and unwatch of its node topics.</summary>
internal sealed class NodeDiagModule(FakeChangeStore store) : StreamModule("diag", store), IDashboardNodeTopicObserver
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public void OnNodeTopicWatched(DashboardNodeTopic topic) => _Record("watch " + topic.Topic);

    public void OnNodeTopicUnwatched(DashboardNodeTopic topic) => _Record("unwatch " + topic.Topic);

    private void _Record(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}
