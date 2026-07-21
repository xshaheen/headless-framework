// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Tests.Managers;

public sealed class OnNodeDeathInputApiTests
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    [Fact]
    public void time_job_entity_defaults_on_node_death_to_retry()
    {
        new FakeTimeJob().OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public void cron_job_entity_defaults_on_node_death_to_retry()
    {
        new FakeCronJob().OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public void on_node_death_is_publicly_settable_on_a_time_job()
    {
        var job = new FakeTimeJob { OnNodeDeath = NodeDeathPolicy.MarkFailed };

        job.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
    }

    [Fact]
    public void on_node_death_flows_per_node_across_a_parent_child_grandchild_chain()
    {
        // A three-level chain carries an independent OnNodeDeath at each node.
        var grandChild = new FakeTimeJob { Function = "grandchild", OnNodeDeath = NodeDeathPolicy.Retry };
        var child = new FakeTimeJob
        {
            Function = "child",
            OnNodeDeath = NodeDeathPolicy.Skip,
            Children = [grandChild],
        };
        var job = new FakeTimeJob
        {
            Function = "parent",
            OnNodeDeath = NodeDeathPolicy.MarkFailed,
            Children = [child],
        };

        job.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        var childNode = job.Children.Should().ContainSingle().Subject;
        childNode.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        childNode.Children.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public void on_node_death_defaults_to_retry_for_every_node_in_a_chain()
    {
        // Left unset at both levels, every chain node inherits the entity default.
        var child = new FakeTimeJob { Function = "child" };
        var job = new FakeTimeJob { Function = "parent", Children = [child] };

        job.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
        job.Children.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }
}
