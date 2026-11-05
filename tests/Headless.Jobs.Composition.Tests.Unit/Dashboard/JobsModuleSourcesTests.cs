// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// The Jobs module's read capabilities and its stream feed: Needs you groups failures by cause and names a function no
/// node runs, the Overview reads the store's figures, a change made on this node is published once with the marker
/// read back from its row, and every change reaches the change reader.
/// </summary>
public sealed class JobsModuleSourcesTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity("test"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_list_one_needs_you_item_per_function_and_fingerprint_recommending_requeue_over_the_group()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var now = _time.GetUtcNow();
        await node.Store.AddTimeJobsAsync(
            [
                JobsModuleNode.FailedJob(now),
                JobsModuleNode.FailedJob(now),
                JobsModuleNode.FailedJob(now, fingerprint: "v1:timeout"),
            ],
            AbortToken
        );

        // when
        var items = await node.Module.GetAttentionAsync(new DashboardAttentionRequest { User = _User }, AbortToken);

        // then
        var declined = items
            .Should()
            .ContainSingle(item => item.GroupKey == "billing.charge:v1:charge-declined")
            .Subject;
        declined.Count.Should().Be(2);
        declined.Title.Should().Be("2 failed runs of billing.charge");
        declined.Severity.Should().Be(DashboardAttentionSeverity.Critical);
        declined.RecommendedCommandId.Should().Be("jobs.requeue");
        declined.RecentCount.Should().Be(2);
        items.Should().ContainSingle(item => item.GroupKey == "billing.charge:v1:timeout");

        // The group's filter scopes its Operation to exactly the rows the item counts.
        var scope = await node.Module.PrepareAsync(
            new DashboardOperationScopeRequest
            {
                CommandId = "jobs.requeue",
                Filter = declined.GroupFilter!.Value,
                CountLimit = 100_000,
                SampleSize = 5,
                User = _User,
            },
            AbortToken
        );
        scope.Count.Should().Be(declined.Count);
    }

    [Fact]
    public async Task should_list_a_function_no_live_node_claims_with_a_link_to_its_function_page()
    {
        // given: this node's job loops are off, so its run filter claims no function
        await using var node = JobsModuleNode.Create(_time);

        // when
        var items = await node.Module.GetAttentionAsync(new DashboardAttentionRequest { User = _User }, AbortToken);

        // then
        var unclaimed = items.Should().ContainSingle(item => item.Id == "unclaimed:billing.charge").Subject;
        unclaimed.Title.Should().Be("No live node runs billing.charge");
        unclaimed
            .Target.Should()
            .Be(
                new ObjectRef
                {
                    Module = "jobs",
                    Kind = "function",
                    Id = "billing.charge",
                }
            );
        unclaimed.RecommendedCommandId.Should().BeNull();
    }

    [Fact]
    public async Task should_report_failed_runs_of_the_last_day_against_the_day_before()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var now = _time.GetUtcNow();
        var yesterday = JobsModuleNode.FailedJob(now);
        yesterday.ExecutedAt = now.AddHours(-30);
        await node.Store.AddTimeJobsAsync([JobsModuleNode.FailedJob(now), yesterday], AbortToken);

        // when
        var figures = await node.Module.GetOverviewAsync(new DashboardOverviewRequest { User = _User }, AbortToken);

        // then
        figures
            .Select(figure => figure.Id)
            .Should()
            .Equal("jobs.overview.succeeded-share", "jobs.overview.failed", "jobs.overview.late");
        var failed = figures[1];
        failed.Value.Should().Be(1);
        failed.Comparison!.Value.Should().Be(1);
        failed.Comparison.HigherIsBetter.Should().BeFalse();
    }

    [Fact]
    public async Task should_publish_a_run_that_fails_on_this_node_once_with_the_marker_read_back_from_its_row()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await node.Store.AddTimeJobsAsync([job], AbortToken);
        var claimed = (await node.Store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        var failure = new JobExecutionState
        {
            FunctionName = JobsModuleNode.Charge,
            JobId = job.Id,
            Type = JobType.TimeJob,
            ClaimOwner = claimed.OwnerId!,
        }
            .SetProperty(x => x.Status, JobStatus.Failed)
            .SetProperty(x => x.ExceptionDetails, "boom");

        // when: the executor's end-state write goes through the job managers, which notify after it returns
        (await node.Services.GetRequiredService<IInternalJobManager>().UpdateTickerAsync(failure, AbortToken))
            .Should()
            .Be(1);
        var change = await node.Published.WaitForAsync(job.Id, AbortToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);

        // then
        var stored = await node.Store.GetJobRunAsync(job.Id, AbortToken);
        node.Published.For(job.Id).Should().ContainSingle();
        change.Version.Should().Be(stored!.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture));
        change.Projection!.Value.GetProperty("status").GetString().Should().Be("Failed");
        change.AffectsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task should_publish_nothing_for_a_batch_add_and_deliver_its_rows_through_the_change_reader()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cursor = await node.Module.GetStartCursorAsync(AbortToken);
        var batch = new List<TimeJobEntity>
        {
            JobsModuleNode.IdleJob(_time.GetUtcNow()),
            JobsModuleNode.IdleJob(_time.GetUtcNow()),
        };
        batch.ForEach(job => job.ExecutionTime = _time.GetUtcNow().UtcDateTime.AddHours(1));

        // when
        var added = await node
            .Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>()
            .AddBatchAsync(batch, AbortToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        var read = await node.Module.ReadChangesAsync(cursor, AbortToken);

        // then
        node.Published.Changes.Should().BeEmpty("a batch add names no row");
        var ids = added.Select(job => job.Id.ToString()).ToArray();
        read.Changes.Select(change => change.Target.Id).Should().BeEquivalentTo(ids);
        foreach (var change in read.Changes)
        {
            var stored = await node.Store.GetJobRunAsync(Guid.Parse(change.Target.Id), AbortToken);
            change.Target.Kind.Should().Be("run");
            change.Version.Should().Be(stored!.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture));
        }
    }
}
