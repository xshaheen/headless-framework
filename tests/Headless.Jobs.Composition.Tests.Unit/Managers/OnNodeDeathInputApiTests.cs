// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Managers;

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
    public void builder_set_on_node_death_flows_to_parent_child_and_grandchild()
    {
        FakeTimeJob job = FluentChainJobBuilder<FakeTimeJob>
            .BeginWith(p => p.SetFunction("parent").SetOnNodeDeath(NodeDeathPolicy.MarkFailed))
            .WithFirstChild(c => c.SetFunction("child").SetOnNodeDeath(NodeDeathPolicy.Skip))
            .WithFirstGrandChild(g => g.SetFunction("grandchild").SetOnNodeDeath(NodeDeathPolicy.Retry));

        job.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        var child = job.Children.Should().ContainSingle().Subject;
        child.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        child.Children.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public void builder_defaults_on_node_death_to_retry_when_not_set()
    {
        FakeTimeJob job = FluentChainJobBuilder<FakeTimeJob>.BeginWith(p => p.SetFunction("parent"));

        job.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }
}
