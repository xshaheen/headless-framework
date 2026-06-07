// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NodeIdentityTests : TestBase
{
    [Fact]
    public void should_round_trip_canonical_string()
    {
        // given
        var identity = new NodeIdentity(new NodeId("orders-worker-0"), new NodeIncarnation(42));

        // when
        var parsed = NodeIdentity.Parse(identity.ToString());

        // then
        parsed.Should().Be(identity);
        parsed.ToString().Should().Be("orders-worker-0@42");
    }

    [Theory]
    [InlineData("a@")]
    [InlineData("@1")]
    [InlineData("a@notnum")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a@0")]
    [InlineData("a@-1")]
    public void should_reject_malformed_identity_without_throwing(string value)
    {
        // when
        var parsed = NodeIdentity.TryParse(value, out var identity);

        // then
        parsed.Should().BeFalse();
        identity.Should().Be(default(NodeIdentity));
    }

    [Fact]
    public void should_compare_identity_by_node_id_and_incarnation()
    {
        // given
        var left = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        var matching = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(1));
        var differentIncarnation = new NodeIdentity(new NodeId("node-a"), new NodeIncarnation(2));

        // then
        left.Should().Be(matching);
        left.Should().NotBe(differentIncarnation);
    }

    [Fact]
    public void should_order_incarnations_by_generation()
    {
        // given
        var first = new NodeIncarnation(1);
        var second = new NodeIncarnation(2);

        // then
        first.CompareTo(second).Should().BeNegative();
        second.CompareTo(first).Should().BePositive();
    }
}
