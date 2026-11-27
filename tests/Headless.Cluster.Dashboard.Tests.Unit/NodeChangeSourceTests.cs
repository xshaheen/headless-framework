// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Cluster;
using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The node change source over a fake membership store: what each read emits for a change, a heartbeat, a removal,
/// and a burst, that its versions only grow, and that two nodes reading one store agree on the cursor.
/// </summary>
public sealed class NodeChangeSourceTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeMembership _membership = new();

    [Fact]
    public async Task should_return_nothing_and_the_same_cursor_when_the_store_did_not_change()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"), FakeMembership.Node("b@1"));
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);

        // when
        var batch = await source.ReadChangesAsync(start, AbortToken);

        // then
        batch.Changes.Should().BeEmpty();
        batch.Cursor.Should().Be(start);
        batch.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task should_emit_one_change_for_a_node_whose_state_changed()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"), FakeMembership.Node("b@1"));
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot(FakeMembership.Node("a@1"), FakeMembership.Node("b@1", NodeLivenessState.Suspected));

        // when
        var batch = await source.ReadChangesAsync(start, AbortToken);

        // then
        var change = batch.Changes.Should().ContainSingle().Subject;
        change.Target.Module.Should().Be("cluster");
        change.Target.Kind.Should().Be("node");
        change.Target.Id.Should().Be("b@1");
        change.AffectsAttention.Should().BeTrue();
        change.Version.Should().Be(_time.GetUtcNow().UtcTicks.ToString("D19", CultureInfo.InvariantCulture));
        batch.Cursor.Should().NotBe(start);
    }

    [Fact]
    public async Task should_emit_nothing_when_only_the_heartbeat_moved()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1", lastHeartbeatAt: _time.GetUtcNow()));
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot(FakeMembership.Node("a@1", lastHeartbeatAt: _time.GetUtcNow().AddSeconds(5)));

        // when
        var batch = await source.ReadChangesAsync(start, AbortToken);

        // then
        batch.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_emit_a_change_for_a_new_node_a_role_change_and_a_metadata_change()
    {
        // given
        _membership.SetSnapshot(
            FakeMembership.Node("a@1", role: "api"),
            FakeMembership.Node("b@1", metadata: ("v", "1"))
        );
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot(
            FakeMembership.Node("a@1", role: "worker"),
            FakeMembership.Node("b@1", metadata: ("v", "2")),
            FakeMembership.Node("c@1")
        );

        // when
        var batch = await source.ReadChangesAsync(start, AbortToken);

        // then
        batch.Changes.Select(static change => change.Target.Id).Should().Equal("a@1", "b@1", "c@1");
    }

    [Fact]
    public async Task should_emit_one_removal_for_an_identity_that_left_the_snapshot()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"), FakeMembership.Node("b@1", NodeLivenessState.Dead));
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot(FakeMembership.Node("a@1"));

        // when
        var batch = await source.ReadChangesAsync(start, AbortToken);
        var after = await source.ReadChangesAsync(batch.Cursor, AbortToken);

        // then
        var removal = batch.Changes.Should().ContainSingle().Subject;
        removal.Target.Id.Should().Be("b@1");
        removal.Projection.Should().BeNull();
        after.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_give_every_flip_a_later_version_than_the_one_before_it()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"));
        var source = _Source();
        var cursor = await source.GetStartCursorAsync(AbortToken);
        var versions = new List<string>();

        // when the node goes suspected, alive again, then suspected again
        foreach (
            var state in new[] { NodeLivenessState.Suspected, NodeLivenessState.Alive, NodeLivenessState.Suspected }
        )
        {
            _time.Advance(TimeSpan.FromSeconds(2));
            _membership.SetSnapshot(FakeMembership.Node("a@1", state));
            var batch = await source.ReadChangesAsync(cursor, AbortToken);
            versions.Add(batch.Changes.Should().ContainSingle().Subject.Version);
            cursor = batch.Cursor;
        }

        // then versions compare as growing integers, so the shell drops none of them as already sent
        versions
            .Select(static version => long.Parse(version, CultureInfo.InvariantCulture))
            .Should()
            .BeInAscendingOrder();
        versions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task should_give_two_nodes_reading_one_store_the_same_cursor_and_changes()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"), FakeMembership.Node("b@1"));
        var first = _Source();
        var second = _Source();
        var start = await first.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot(FakeMembership.Node("a@1", NodeLivenessState.Dead), FakeMembership.Node("c@2"));

        // when
        var one = await first.ReadChangesAsync(start, AbortToken);
        var other = await second.ReadChangesAsync(start, AbortToken);

        // then
        (await second.GetStartCursorAsync(AbortToken))
            .Should()
            .Be(one.Cursor);
        other.Cursor.Should().Be(one.Cursor);
        other
            .Changes.Select(static change => change.Target.Id)
            .Should()
            .Equal(one.Changes.Select(static change => change.Target.Id));
    }

    [Fact]
    public async Task should_read_a_burst_over_several_pages()
    {
        // given
        var source = _Source(pageSize: 200);
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot([
            .. Enumerable.Range(1, 250).Select(static index => FakeMembership.Node($"n{index:D3}@1")),
        ]);

        // when
        var first = await source.ReadChangesAsync(start, AbortToken);
        var second = await source.ReadChangesAsync(first.Cursor, AbortToken);
        var third = await source.ReadChangesAsync(second.Cursor, AbortToken);

        // then
        first.Changes.Should().HaveCount(200);
        first.HasMore.Should().BeTrue();
        second.Changes.Should().HaveCount(50);
        second.HasMore.Should().BeFalse();
        third.Changes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("v2:garbage")]
    [InlineData("v1:%%%")]
    [InlineData("v1:bm90LWEtbm9kZSBhYmM")]
    public async Task should_refuse_a_cursor_it_did_not_write(string cursor)
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"));
        var source = _Source();

        // when
        var read = () => source.ReadChangesAsync(cursor, AbortToken);

        // then
        await read.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("web 1@1")]
    [InlineData("web\n1@1")]
    public async Task should_round_trip_a_node_id_with_an_interior_space_or_line_break(string identity)
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node(identity), FakeMembership.Node("b@1"));
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.SetSnapshot(FakeMembership.Node(identity, NodeLivenessState.Suspected), FakeMembership.Node("b@1"));

        // when
        var batch = await source.ReadChangesAsync(start, AbortToken);
        var after = await source.ReadChangesAsync(batch.Cursor, AbortToken);

        // then
        batch.Changes.Should().ContainSingle().Which.Target.Id.Should().Be(identity);
        after.Changes.Should().BeEmpty();
        after.Cursor.Should().Be(batch.Cursor);
    }

    [Theory]
    [InlineData("YUAx 0123456789abcde")] // a version one character short
    [InlineData("YUAx 0123456789abcdef0")] // a version one character long
    [InlineData("YUAx 0123456789abcdef\nYUAx 0123456789abcdef")] // the identity a@1 twice
    [InlineData("YUAx 0123456789abcdef\n")] // an empty line
    [InlineData("YUAx0123456789abcdef")] // no separator
    [InlineData("!!! 0123456789abcdef")] // an identity that is not base64url
    [InlineData("bm9kZQ 0123456789abcdef")] // "node", which is no identity
    public async Task should_refuse_a_cursor_with_an_invalid_line(string payload)
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"));
        var source = _Source();
        var cursor = "v1:" + System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));

        // when
        var read = () => source.ReadChangesAsync(cursor, AbortToken);

        // then
        await read.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_read_a_cursor_whose_lines_are_all_valid()
    {
        // given the identity a@1 at a version the store does not show
        _membership.SetSnapshot(FakeMembership.Node("a@1"));
        var source = _Source();
        var cursor =
            "v1:" + System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes("YUAx 0123456789abcdef"));

        // when
        var batch = await source.ReadChangesAsync(cursor, AbortToken);

        // then
        batch.Changes.Should().ContainSingle().Which.Target.Id.Should().Be("a@1");
    }

    [Fact]
    public async Task should_read_nothing_from_a_host_without_coordination()
    {
        // given
        var source = new NodeChangeSource(
            "cluster",
            new ClusterServices(new ServiceCollection().BuildServiceProvider()),
            _time,
            200
        );

        // when
        var start = await source.GetStartCursorAsync(AbortToken);
        var batch = await source.ReadChangesAsync(start, AbortToken);

        // then
        start.Should().Be("v1:");
        batch.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_let_a_failed_store_read_propagate_so_the_shell_marks_the_module_stale()
    {
        // given
        _membership.SetSnapshot(FakeMembership.Node("a@1"));
        var source = _Source();
        var start = await source.GetStartCursorAsync(AbortToken);
        _membership.IsUnreachable = true;

        // when
        var read = () => source.ReadChangesAsync(start, AbortToken);

        // then
        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    private NodeChangeSource _Source(int pageSize = 200)
    {
        var services = new ServiceCollection()
            .AddSingleton(FakeMembership.Summary())
            .AddSingleton<INodeMembership>(_membership)
            .AddSingleton<IMembershipEventLog>(_membership)
            .BuildServiceProvider();

        return new NodeChangeSource("cluster", new ClusterServices(services), _time, pageSize);
    }
}
