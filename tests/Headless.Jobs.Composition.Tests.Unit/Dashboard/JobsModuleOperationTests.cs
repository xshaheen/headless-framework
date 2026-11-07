// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// Requeue, Cancel, and Delete over a filter, as the shell drives them one chunk at a time on any node: the scope
/// freezes at confirm, each row is changed at most once, and a stopped Operation confirmed again counts only what is
/// left.
/// </summary>
public sealed class JobsModuleOperationTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity("test"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_requeue_five_thousand_failed_runs_once_each_across_two_nodes_with_a_stop_and_a_rerun()
    {
        // given
        await using var nodeA = JobsModuleNode.Create(_time);
        await using var nodeB = JobsModuleNode.Create(_time, nodeA.Store, nodeId: "node-b");
        var failed = Enumerable.Range(0, 5_000).Select(_ => JobsModuleNode.FailedJob(_time.GetUtcNow())).ToArray();
        await nodeA.Store.AddTimeJobsAsync(failed, AbortToken);
        var other = JobsModuleNode.FailedJob(_time.GetUtcNow(), function: JobsModuleNode.Report);
        await nodeA.Store.AddTimeJobsAsync([other], AbortToken);
        var filter = _Filter(JobsModuleNode.Charge);
        var done = new List<string>();

        // when: confirm on node A, run three chunks alternating nodes, then stop
        var scope = await nodeA.Module.PrepareAsync(_Scope("jobs.requeue", filter), AbortToken);
        scope.Count.Should().Be(5_000);
        scope.Sample.Should().HaveCount(5);

        string? cursor = null;
        for (var chunk = 0; chunk < 3; chunk++)
        {
            var node = chunk % 2 == 0 ? nodeA : nodeB;
            var result = await node.Module.RunChunkAsync(_Chunk("jobs.requeue", filter, scope, cursor), AbortToken);
            done.AddRange(result.Rows.Where(row => row.Outcome == DashboardRowOutcome.Done).Select(row => row.Id));
            cursor = result.NextCursor;
        }

        // then: a re-run confirms with the remaining count and finishes the rest
        var rerun = await nodeB.Module.PrepareAsync(_Scope("jobs.requeue", filter), AbortToken);
        rerun.Count.Should().Be(3_500);

        cursor = null;
        var calls = 0;
        do
        {
            var node = calls++ % 2 == 0 ? nodeB : nodeA;
            var result = await node.Module.RunChunkAsync(_Chunk("jobs.requeue", filter, rerun, cursor), AbortToken);
            done.AddRange(result.Rows.Where(row => row.Outcome == DashboardRowOutcome.Done).Select(row => row.Id));
            cursor = result.NextCursor;
        } while (cursor is not null);

        done.Should().HaveCount(5_000).And.OnlyHaveUniqueItems();
        done.Should().BeEquivalentTo(failed.Select(job => job.Id.ToString()));
        (await nodeA.Store.GetJobRunAsync(other.Id, AbortToken))!
            .Status.Should()
            .Be(JobStatus.Failed, "a run outside the filter is never in scope");
        (await nodeA.Module.PrepareAsync(_Scope("jobs.requeue", filter), AbortToken)).Count.Should().Be(0);
    }

    [Fact]
    public async Task should_not_requeue_again_a_run_requeued_by_an_earlier_chunk_that_failed_again()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var jobs = Enumerable.Range(0, 3).Select(_ => JobsModuleNode.FailedJob(_time.GetUtcNow())).ToArray();
        await node.Store.AddTimeJobsAsync(jobs, AbortToken);
        var filter = _Filter(JobsModuleNode.Charge);
        var scope = await node.Module.PrepareAsync(_Scope("jobs.requeue", filter), AbortToken);

        var first = await node.Module.RunChunkAsync(_Chunk("jobs.requeue", filter, scope, cursor: null, 1), AbortToken);
        var requeued = Guid.Parse(first.Rows.Should().ContainSingle().Subject.Id);

        // The requeued run is claimed and fails again before the Operation's later chunks run.
        var claimed = (await node.Store.AcquireImmediateTimeJobsAsync([requeued], AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        var failAgain = new JobExecutionState
        {
            FunctionName = JobsModuleNode.Charge,
            JobId = requeued,
            ClaimOwner = claimed.OwnerId!,
        }.SetProperty(x => x.Status, JobStatus.Failed);
        (await node.Store.UpdateTimeJobAsync(failAgain, AbortToken)).Should().Be(1);

        // when: the rest of the Operation runs, and a resent first chunk arrives
        var rest = await node.Module.RunChunkAsync(
            _Chunk("jobs.requeue", filter, scope, first.NextCursor, chunkSize: 10),
            AbortToken
        );
        var resent = await node.Module.RunChunkAsync(
            _Chunk("jobs.requeue", filter, scope, cursor: null, chunkSize: 10),
            AbortToken
        );

        // then
        rest.Rows.Select(row => row.Id).Should().NotContain(requeued.ToString());
        resent.Rows.Select(row => row.Id).Should().NotContain(requeued.ToString());
        (await node.Store.GetJobRunAsync(requeued, AbortToken))!.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task should_cancel_a_running_run_that_reports_progress_during_the_operation()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var jobs = Enumerable.Range(0, 2).Select(_ => JobsModuleNode.IdleJob(_time.GetUtcNow())).ToArray();
        await node.Store.AddTimeJobsAsync(jobs, AbortToken);
        var claimed = await node.Store.AcquireImmediateTimeJobsAsync([.. jobs.Select(job => job.Id)], AbortToken);
        claimed.Should().HaveCount(2);
        var filter = _Filter(JobsModuleNode.Charge);
        var scope = await node.Module.PrepareAsync(_Scope("jobs.cancel", filter), AbortToken);
        scope.Count.Should().Be(2);

        // The run's marker moves after confirm, as a running job's progress moves it.
        var reporting = claimed[0];
        (await node.Store.UpdateTimeJobProgressAsync(reporting.Id, reporting.OwnerId!, new(50, "halfway"), AbortToken))
            .Should()
            .BeTrue();

        // when
        var chunk = await node.Module.RunChunkAsync(_Chunk("jobs.cancel", filter, scope, cursor: null), AbortToken);

        // then
        chunk.Rows.Should().HaveCount(2).And.OnlyContain(row => row.Outcome == DashboardRowOutcome.Done);
        foreach (var job in jobs)
        {
            var stored = await node.Store.GetJobRunAsync(job.Id, AbortToken);
            stored!.CancelRequested.Should().BeTrue();
            stored.Status.Should().Be(JobStatus.InProgress);
        }
    }

    [Fact]
    public async Task should_delete_runs_that_are_not_running_and_publish_them_as_gone()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var failed = JobsModuleNode.FailedJob(_time.GetUtcNow());
        var running = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await node.Store.AddTimeJobsAsync([failed, running], AbortToken);
        await node.Store.AcquireImmediateTimeJobsAsync([running.Id], AbortToken);
        var filter = _Filter(JobsModuleNode.Charge);
        var scope = await node.Module.PrepareAsync(_Scope("jobs.delete", filter), AbortToken);

        // when
        var chunk = await node.Module.RunChunkAsync(_Chunk("jobs.delete", filter, scope, cursor: null), AbortToken);

        // then
        scope.Count.Should().Be(1);
        chunk.Rows.Should().ContainSingle().Which.Id.Should().Be(failed.Id.ToString());
        (await node.Store.GetJobRunAsync(failed.Id, AbortToken)).Should().BeNull();
        (await node.Store.GetJobRunAsync(running.Id, AbortToken)).Should().NotBeNull();
        node.Published.For(failed.Id).Should().ContainSingle().Which.Projection.Should().BeNull();
    }

    private static JsonElement _Filter(string function) =>
        JsonSerializer.SerializeToElement(new { functions = new[] { function } });

    private static DashboardOperationScopeRequest _Scope(string commandId, JsonElement filter) =>
        new()
        {
            CommandId = commandId,
            Filter = filter,
            CountLimit = 100_000,
            SampleSize = 5,
            User = _User,
        };

    private static DashboardOperationChunkRequest _Chunk(
        string commandId,
        JsonElement filter,
        DashboardOperationScope scope,
        string? cursor,
        int chunkSize = 500
    ) =>
        new()
        {
            CommandId = commandId,
            Filter = filter,
            HighWaterMark = scope.HighWaterMark,
            Cursor = cursor,
            ChunkSize = chunkSize,
            Reason = "test",
            User = _User,
        };
}
