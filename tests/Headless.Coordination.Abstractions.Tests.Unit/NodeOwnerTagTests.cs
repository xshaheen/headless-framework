// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NodeOwnerTagTests : TestBase
{
    private static readonly NodeIdentity _Identity = new(new NodeId("pod-a"), new NodeIncarnation(3));

    [Fact]
    public void should_qualify_the_identity_with_its_cluster()
    {
        NodeOwnerTag.Format("orders", _Identity).Should().Be("orders/pod-a@3");
    }

    [Fact]
    public void should_give_the_same_identity_a_different_tag_in_each_cluster()
    {
        NodeOwnerTag.Format("orders", _Identity).Should().NotBe(NodeOwnerTag.Format("billing", _Identity));
    }

    [Theory]
    [InlineData("orders/pod-a@3", "orders", true)]
    [InlineData("billing/pod-a@3", "orders", false)]
    [InlineData("orders-eu/pod-a@3", "orders", false)]
    [InlineData("orders", "orders", false)]
    [InlineData("pod-a@3", "orders", false)]
    [InlineData("/pod-a@3", "", true)]
    public void should_report_whether_an_owner_tag_belongs_to_a_cluster(string tag, string cluster, bool expected)
    {
        NodeOwnerTag.IsInCluster(tag, cluster).Should().Be(expected);
    }

    [Fact]
    public void should_fit_the_longest_cluster_node_and_incarnation_in_max_length()
    {
        var identity = new NodeIdentity(
            new NodeId(new string('n', NodeId.MaxLength)),
            new NodeIncarnation(long.MaxValue)
        );
        var tag = NodeOwnerTag.Format(new string('c', CoordinationOptions.ClusterNameMaxLength), identity);

        tag.Should().HaveLength(NodeOwnerTag.MaxLength);
    }

    [Fact]
    public void should_read_the_local_owner_tag_from_the_membership_or_null_when_unregistered()
    {
        var membership = Substitute.For<INodeMembership>();
        membership.ClusterName.Returns("orders");
        membership.Identity.Returns((NodeIdentity?)null);

        membership.GetOwnerTag().Should().BeNull();

        membership.Identity.Returns(_Identity);

        membership.GetOwnerTag().Should().Be("orders/pod-a@3");
    }

    [Fact]
    public async Task should_tag_the_null_membership_with_the_default_cluster()
    {
        var membership = new NullNodeMembership();
        var identity = await membership.RegisterAsync(AbortToken);

        membership.ClusterName.Should().Be(CoordinationOptions.DefaultClusterName);
        membership.GetOwnerTag().Should().Be(NodeOwnerTag.Format(CoordinationOptions.DefaultClusterName, identity));
    }
}
