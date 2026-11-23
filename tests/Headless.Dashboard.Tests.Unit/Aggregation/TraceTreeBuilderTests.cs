// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Aggregation;

/// <summary>
/// The trace tree: each item sits under the object that caused it, whichever module returned either, and every item
/// whose place cannot be known exactly says why in a tag.
/// </summary>
public sealed class TraceTreeBuilderTests : TestBase
{
    private static readonly DateTimeOffset _T0 = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void should_place_each_received_generation_under_the_message_it_delivers_not_under_its_copied_causation()
    {
        var run = _Run("run-1", at: 0);
        var published = _Published("msg-1", at: 1, causation: _Key(DashboardTraceKeyKind.Run, "run-1"));
        // A received envelope copies the publisher's causation, which names the job, not the message.
        var first = _Received("rcv-1", "msg-1", at: 2, causation: _Key(DashboardTraceKeyKind.Run, "run-1"));
        var second = _Received("rcv-2", "msg-1", at: 3, causation: _Key(DashboardTraceKeyKind.Run, "run-1"));

        var rows = TraceTreeBuilder.Build([_Jobs(run), _Messaging(published), _Messaging(first), _Messaging(second)]);

        _Shape(rows).Should().Equal(("run-1", 0), ("msg-1", 1), ("rcv-1", 2), ("rcv-2", 2));
        rows.Should().AllSatisfy(row => row.Tags.Should().BeEmpty());
    }

    [Fact]
    public void should_place_each_scheduled_job_under_the_generation_of_the_subscriber_that_scheduled_it()
    {
        var published = _Published("msg-1", at: 0);
        var first = _Received("rcv-1", "msg-1", at: 1);
        var second = _Received("rcv-2", "msg-1", at: 2);
        var jobOfSecond = _Run("run-b", at: 3, causation: _Key(DashboardTraceKeyKind.Delivery, "rcv-2"));
        var jobOfFirst = _Run("run-a", at: 4, causation: _Key(DashboardTraceKeyKind.Delivery, "rcv-1"));

        var rows = TraceTreeBuilder.Build([
            _Jobs(jobOfSecond),
            _Jobs(jobOfFirst),
            _Messaging(published),
            _Messaging(first),
            _Messaging(second),
        ]);

        _Shape(rows).Should().Equal(("msg-1", 0), ("rcv-1", 1), ("run-a", 2), ("rcv-2", 1), ("run-b", 2));
    }

    [Fact]
    public void should_place_a_message_published_in_a_consumer_under_the_published_row_tagged_when_two_generations_exist()
    {
        var published = _Published("msg-1", at: 0);
        var first = _Received("rcv-1", "msg-1", at: 1);
        var second = _Received("rcv-2", "msg-1", at: 2);
        var reply = _Published("msg-2", at: 3, causation: _Key(DashboardTraceKeyKind.Message, "msg-1"));

        var rows = TraceTreeBuilder.Build([
            _Messaging(published),
            _Messaging(first),
            _Messaging(second),
            _Messaging(reply),
        ]);

        _Shape(rows).Should().Equal(("msg-1", 0), ("rcv-1", 1), ("rcv-2", 1), ("msg-2", 1));
        _Row(rows, "msg-2").Tags.Should().Equal(TraceTreeBuilder.ConsumerNotRecordedTag);
    }

    [Fact]
    public void should_place_a_message_published_in_a_consumer_under_the_only_generation_of_the_consumed_message()
    {
        var published = _Published("msg-1", at: 0);
        var generation = _Received("rcv-1", "msg-1", at: 1);
        var reply = _Published("msg-2", at: 2, causation: _Key(DashboardTraceKeyKind.Message, "msg-1"));

        var rows = TraceTreeBuilder.Build([_Messaging(published), _Messaging(generation), _Messaging(reply)]);

        _Shape(rows).Should().Equal(("msg-1", 0), ("rcv-1", 1), ("msg-2", 2));
        _Row(rows, "msg-2").Tags.Should().BeEmpty();
    }

    [Fact]
    public void should_make_a_child_whose_parent_is_not_on_the_page_a_root_tagged_parent_not_on_this_page()
    {
        var orphan = _Published("msg-1", at: 1, causation: _Key(DashboardTraceKeyKind.Run, "run-gone"));
        var root = _Run("run-1", at: 0);

        var rows = TraceTreeBuilder.Build([_Jobs(root), _Messaging(orphan)]);

        _Shape(rows).Should().Equal(("run-1", 0), ("msg-1", 0));
        _Row(rows, "msg-1").Tags.Should().Equal(TraceTreeBuilder.ParentNotOnPageTag);
    }

    [Fact]
    public void should_never_match_a_causation_against_an_object_of_another_kind_with_the_same_id()
    {
        // The causation names run "x-1"; a message whose id is also "x-1" is not that run.
        var message = _Published("x-1", at: 0);
        var child = _Run("run-1", at: 1, causation: _Key(DashboardTraceKeyKind.Run, "x-1"));

        var rows = TraceTreeBuilder.Build([_Messaging(message), _Jobs(child)]);

        _Shape(rows).Should().Equal(("x-1", 0), ("run-1", 0));
        _Row(rows, "run-1").Tags.Should().Equal(TraceTreeBuilder.ParentNotOnPageTag);
    }

    [Fact]
    public void should_break_a_two_item_cycle_and_tag_the_item_it_broke_at()
    {
        var first = _Run("run-a", at: 0, causation: _Key(DashboardTraceKeyKind.Run, "run-b"));
        var second = _Run("run-b", at: 1, causation: _Key(DashboardTraceKeyKind.Run, "run-a"));

        var rows = TraceTreeBuilder.Build([_Jobs(second), _Jobs(first)]);

        _Shape(rows).Should().Equal(("run-a", 0), ("run-b", 1));
        _Row(rows, "run-a").Tags.Should().Equal(TraceTreeBuilder.CycleTag);
        _Row(rows, "run-b").Tags.Should().BeEmpty();
    }

    [Fact]
    public void should_keep_a_child_stamped_before_its_parent_under_the_parent_tagged_clock_skew()
    {
        var parent = _Run("run-1", at: 10);
        var child = _Published("msg-1", at: 5, causation: _Key(DashboardTraceKeyKind.Run, "run-1"));
        var laterRoot = _Run("run-2", at: 20);

        var rows = TraceTreeBuilder.Build([_Jobs(laterRoot), _Messaging(child), _Jobs(parent)]);

        _Shape(rows).Should().Equal(("run-1", 0), ("msg-1", 1), ("run-2", 0));
        _Row(rows, "msg-1").Tags.Should().Equal(TraceTreeBuilder.ClockSkewTag);
    }

    [Fact]
    public void should_list_once_an_item_two_peers_that_share_a_store_both_returned()
    {
        var fromNodeA = _Published("msg-1", at: 0) with { Node = "node-a" };
        var fromNodeB = _Published("msg-1", at: 0) with { Node = "node-b" };

        var rows = TraceTreeBuilder.Build([_Messaging(fromNodeA), _Messaging(fromNodeB)]);

        rows.Should().ContainSingle().Which.Item.Node.Should().Be("node-a");
    }

    [Fact]
    public void should_order_roots_and_siblings_by_time()
    {
        var late = _Run("run-late", at: 30);
        var early = _Run("run-early", at: 10);
        var parent = _Run("run-p", at: 0);
        var secondChild = _Run("run-c2", at: 5, causation: _Key(DashboardTraceKeyKind.Run, "run-p"));
        var firstChild = _Run("run-c1", at: 2, causation: _Key(DashboardTraceKeyKind.Run, "run-p"));

        var rows = TraceTreeBuilder.Build([
            _Jobs(late),
            _Jobs(secondChild),
            _Jobs(early),
            _Jobs(firstChild),
            _Jobs(parent),
        ]);

        _Shape(rows).Should().Equal(("run-p", 0), ("run-c1", 1), ("run-c2", 1), ("run-early", 0), ("run-late", 0));
    }

    private static IEnumerable<(string Id, int Depth)> _Shape(IReadOnlyList<TraceRow> rows) =>
        rows.Select(static row => (row.Item.Target.Id, row.Depth));

    private static TraceRow _Row(IReadOnlyList<TraceRow> rows, string id) =>
        rows.Single(row => string.Equals(row.Item.Target.Id, id, StringComparison.Ordinal));

    private static TraceEntry _Jobs(DashboardTraceItem item) => new("jobs", item);

    private static TraceEntry _Messaging(DashboardTraceItem item) => new("messaging", item);

    private static DashboardTraceKey _Key(DashboardTraceKeyKind kind, string value) =>
        new() { Kind = kind, Value = value };

    private static DashboardTraceItem _Run(string id, int at, DashboardTraceKey? causation = null) =>
        _Item("jobs", "run", id, at, _Key(DashboardTraceKeyKind.Run, id), causation);

    private static DashboardTraceItem _Published(string id, int at, DashboardTraceKey? causation = null) =>
        _Item("messaging", "published", id, at, _Key(DashboardTraceKeyKind.Message, id), causation);

    private static DashboardTraceItem _Received(
        string storageId,
        string messageId,
        int at,
        DashboardTraceKey? causation = null
    ) =>
        _Item("messaging", "received", storageId, at, _Key(DashboardTraceKeyKind.Delivery, storageId), causation) with
        {
            DeliversMessageId = messageId,
        };

    private static DashboardTraceItem _Item(
        string module,
        string kind,
        string id,
        int at,
        DashboardTraceKey key,
        DashboardTraceKey? causation
    )
    {
        return new DashboardTraceItem
        {
            Target = new ObjectRef
            {
                Module = module,
                Kind = kind,
                Id = id,
            },
            Store = "primary",
            StorageId = $"{kind}:{id}",
            Title = $"{kind} {id}",
            At = _T0.AddSeconds(at),
            CorrelationId = "ord-7",
            Key = key,
            Causation = causation,
        };
    }
}
