// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NodeMetadataAgreementTests : TestBase
{
    private const string _Key = "version";

    [Fact]
    public void should_be_unanimous_when_every_alive_node_has_the_same_value()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-b", 1, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-c", 1, NodeLivenessState.Alive, "v1"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.Key.Should().Be(_Key);
        result.IsUnanimous.Should().BeTrue();
        var group = result.Groups.Should().ContainSingle().Subject;
        group.Value.Should().Be("v1");
        group.Nodes.Should().Equal(_Identity("node-a", 1), _Identity("node-b", 1), _Identity("node-c", 1));
    }

    [Fact]
    public void should_order_groups_by_descending_node_count_when_values_differ()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "v2"),
            _Snapshot("node-b", 1, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-c", 1, NodeLivenessState.Alive, "v1"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.IsUnanimous.Should().BeFalse();
        result.Groups.Select(static group => group.Value).Should().Equal("v1", "v2");
        result.Groups[0].Nodes.Should().HaveCount(2);
    }

    [Fact]
    public void should_order_equal_sized_groups_by_ordinal_value()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "b"),
            _Snapshot("node-b", 1, NodeLivenessState.Alive, "B"),
            _Snapshot("node-c", 1, NodeLivenessState.Alive, "a"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then: ordinal puts upper case before lower case
        result.Groups.Select(static group => group.Value).Should().Equal("B", "a", "b");
    }

    [Fact]
    public void should_put_nodes_without_the_key_in_a_trailing_null_group()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, value: null),
            _Snapshot("node-b", 1, NodeLivenessState.Alive, value: null),
            _Snapshot("node-c", 1, NodeLivenessState.Alive, "v1"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then: the missing-key group stays last even though it is the largest
        result.IsUnanimous.Should().BeFalse();
        result.Groups.Select(static group => group.Value).Should().Equal("v1", null);
        result.Groups[^1].Nodes.Should().Equal(_Identity("node-a", 1), _Identity("node-b", 1));
    }

    [Fact]
    public void should_not_be_unanimous_when_every_node_lacks_the_key()
    {
        // given
        NodeLivenessSnapshot[] snapshots = [_Snapshot("node-a", 1, NodeLivenessState.Alive, value: null)];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.Groups.Should().ContainSingle().Which.Value.Should().BeNull();
        result.IsUnanimous.Should().BeFalse();
    }

    [Fact]
    public void should_keep_an_empty_string_value_apart_from_a_missing_key()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, ""),
            _Snapshot("node-b", 1, NodeLivenessState.Alive, value: null),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.Groups.Select(static group => group.Value).Should().Equal("", null);
    }

    [Fact]
    public void should_compare_values_ordinally()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "V1"),
            _Snapshot("node-b", 1, NodeLivenessState.Alive, "v1"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.Groups.Should().HaveCount(2);
        result.IsUnanimous.Should().BeFalse();
    }

    [Fact]
    public void should_ignore_dead_incarnations_and_count_suspected_ones()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-b", 1, NodeLivenessState.Suspected, "v1"),
            _Snapshot("node-c", 1, NodeLivenessState.Dead, "v2"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.IsUnanimous.Should().BeTrue();
        result
            .Groups.Should()
            .ContainSingle()
            .Which.Nodes.Should()
            .Equal(_Identity("node-a", 1), _Identity("node-b", 1));
    }

    [Fact]
    public void should_order_two_incarnations_of_one_node_by_incarnation()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-a", 3, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-a", 2, NodeLivenessState.Suspected, "v1"),
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "v2"),
        ];

        // when
        var result = NodeMetadataAgreement.Compare(snapshots, _Key);

        // then
        result.Groups[0].Nodes.Should().Equal(_Identity("node-a", 2), _Identity("node-a", 3));
        result.Groups[1].Nodes.Should().Equal(_Identity("node-a", 1));
    }

    [Fact]
    public void should_return_the_same_result_for_any_input_order()
    {
        // given
        NodeLivenessSnapshot[] snapshots =
        [
            _Snapshot("node-c", 1, NodeLivenessState.Alive, "v2"),
            _Snapshot("node-a", 2, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-b", 1, NodeLivenessState.Suspected, value: null),
            _Snapshot("node-a", 1, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-d", 1, NodeLivenessState.Alive, "v1"),
            _Snapshot("node-e", 1, NodeLivenessState.Alive, "v2"),
        ];
        var expected = NodeMetadataAgreement.Compare(snapshots, _Key);
        var random = new Random(20261126);

        for (var i = 0; i < 20; i++)
        {
            var shuffled = snapshots.ToArray();
            random.Shuffle(shuffled);

            // when
            var result = NodeMetadataAgreement.Compare(shuffled, _Key);

            // then
            result.IsUnanimous.Should().Be(expected.IsUnanimous);
            result.Groups.Should().HaveCount(expected.Groups.Count);

            for (var g = 0; g < expected.Groups.Count; g++)
            {
                result.Groups[g].Value.Should().Be(expected.Groups[g].Value);
                result.Groups[g].Nodes.Should().Equal(expected.Groups[g].Nodes);
            }
        }

        expected.Groups.Select(static group => group.Value).Should().Equal("v1", "v2", null);
    }

    [Fact]
    public void should_return_no_groups_when_no_incarnation_is_alive_or_suspected()
    {
        // given
        NodeLivenessSnapshot[] onlyDead = [_Snapshot("node-a", 1, NodeLivenessState.Dead, "v1")];

        // when
        var empty = NodeMetadataAgreement.Compare([], _Key);
        var dead = NodeMetadataAgreement.Compare(onlyDead, _Key);

        // then
        empty.Groups.Should().BeEmpty();
        empty.IsUnanimous.Should().BeFalse();
        dead.Groups.Should().BeEmpty();
        dead.IsUnanimous.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void should_reject_a_null_or_whitespace_key(string? key)
    {
        // when
        var act = () => NodeMetadataAgreement.Compare([], key!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_null_snapshots()
    {
        // when
        var act = () => NodeMetadataAgreement.Compare(null!, _Key);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private static NodeIdentity _Identity(string nodeId, long incarnation)
    {
        return new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
    }

    private static NodeLivenessSnapshot _Snapshot(
        string nodeId,
        long incarnation,
        NodeLivenessState state,
        string? value
    )
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["other"] = "x" };

        if (value is not null)
        {
            metadata[_Key] = value;
        }

        return new NodeLivenessSnapshot(
            _Identity(nodeId, incarnation),
            state,
            null,
            metadata,
            DateTimeOffset.UnixEpoch
        );
    }
}
