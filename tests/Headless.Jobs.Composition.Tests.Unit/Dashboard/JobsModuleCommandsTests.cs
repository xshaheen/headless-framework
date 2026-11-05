// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// The Jobs module's commands as the shell calls them: the allowed-actions answer and the execution decide from one
/// rule set, and a call on one object the rules refuse changes nothing and carries the refusal the shell answers
/// <c>409</c> with.
/// </summary>
public sealed class JobsModuleCommandsTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity("test"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_refuse_a_second_cancel_while_the_first_waits_for_the_job_to_stop()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await node.Store.AddTimeJobsAsync([job], AbortToken);
        (await node.Store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken)).Should().ContainSingle();

        var first = await node.Module.ExecuteAsync(_Request("jobs.cancel", job.Id), AbortToken);
        first.Refusal.Should().BeNull();
        first.Rows.Should().ContainSingle().Which.Outcome.Should().Be(DashboardRowOutcome.Done);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, job.Id), AbortToken);
        var second = await node.Module.ExecuteAsync(_Request("jobs.cancel", job.Id), AbortToken);

        // then
        var cancel = allowed
            .Should()
            .ContainSingle(answer => string.Equals(answer.CommandId, "jobs.cancel", StringComparison.Ordinal))
            .Subject;
        cancel.Allowed.Should().BeFalse();
        cancel.DisabledReason.Should().Be("Cancel requested — waiting for the job to stop");
        second.Refusal.Should().Be("Cancel requested — waiting for the job to stop");
        var stored = await node.Store.GetJobRunAsync(job.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.InProgress, "a requested cancel keeps the run running until it stops");
        stored.CancelRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_refuse_run_now_on_a_paused_cron_job_with_resume_first()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob(paused: true);
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(
            _Allowed(JobsViews.CronJobKind, cronJob.Id),
            AbortToken
        );
        var result = await node.Module.ExecuteAsync(_Request("jobs.run-now", cronJob.Id), AbortToken);

        // then
        allowed
            .Single(answer => string.Equals(answer.CommandId, "jobs.run-now", StringComparison.Ordinal))
            .DisabledReason.Should()
            .Be("Paused — resume first");
        allowed
            .Single(answer => string.Equals(answer.CommandId, "jobs.resume", StringComparison.Ordinal))
            .Allowed.Should()
            .BeTrue();
        result.Refusal.Should().Be("Paused — resume first");
        (await node.Store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJob.Id, AbortToken))
            .Should()
            .BeEmpty("a refused run-now adds no occurrence");
    }

    [Fact]
    public async Task should_run_an_active_cron_job_now_and_return_the_new_run()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob();
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);

        // when
        var result = await node.Module.ExecuteAsync(_Request("jobs.run-now", cronJob.Id), AbortToken);

        // then
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Outcome.Should().Be(DashboardRowOutcome.Done);
        var occurrence = (await node.Store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJob.Id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        row.Reason.Should().Be(occurrence.Id.ToString());
    }

    [Fact]
    public async Task should_refuse_editing_the_schedule_of_a_cron_job_defined_in_code()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob();
        cronJob.InitIdentifier = "headless.jobs.seed:default:cron:" + JobsModuleNode.Report;
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(
            _Allowed(JobsViews.CronJobKind, cronJob.Id),
            AbortToken
        );
        var result = await node.Module.ExecuteAsync(
            _Request(
                "jobs.edit-cron",
                cronJob.Id,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["expression"] = JsonSerializer.SerializeToElement("0 */5 * * * *"),
                }
            ),
            AbortToken
        );

        // then
        allowed
            .Single(answer => string.Equals(answer.CommandId, "jobs.edit-cron", StringComparison.Ordinal))
            .DisabledReason.Should()
            .Be("Defined in code");
        result.Refusal.Should().Be("Defined in code");
        (await node.Store.GetCronJobByIdAsync(cronJob.Id, AbortToken))!.Expression.Should().Be("0 0 * * * *");
    }

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Skipped)]
    public async Task should_requeue_a_finished_run_that_did_not_succeed_and_publish_its_new_marker(JobStatus status)
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var job = JobsModuleNode.FailedJob(_time.GetUtcNow());
        job.Status = status;
        await node.Store.AddTimeJobsAsync([job], AbortToken);

        // when
        var result = await node.Module.ExecuteAsync(_Request("jobs.requeue", job.Id), AbortToken);

        // then
        result.Rows.Should().ContainSingle().Which.Outcome.Should().Be(DashboardRowOutcome.Done);
        var stored = await node.Store.GetJobRunAsync(job.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        node.Published.For(job.Id)
            .Should()
            .ContainSingle()
            .Which.Version.Should()
            .Be(stored.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_refuse_requeue_and_delete_of_a_running_run_and_allow_its_cancel()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var job = JobsModuleNode.IdleJob(_time.GetUtcNow());
        await node.Store.AddTimeJobsAsync([job], AbortToken);
        await node.Store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken);

        // when
        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, job.Id), AbortToken);

        // then
        allowed
            .Single(answer => string.Equals(answer.CommandId, "jobs.requeue", StringComparison.Ordinal))
            .DisabledReason.Should()
            .Be("Running — cancel instead");
        allowed
            .Single(answer => string.Equals(answer.CommandId, "jobs.delete", StringComparison.Ordinal))
            .DisabledReason.Should()
            .Be("Running — cancel it first");
        allowed
            .Single(answer => string.Equals(answer.CommandId, "jobs.cancel", StringComparison.Ordinal))
            .Allowed.Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_answer_that_a_deleted_run_no_longer_exists()
    {
        await using var node = JobsModuleNode.Create(_time);
        var gone = Guid.NewGuid();

        var allowed = await node.Module.GetAllowedCommandsAsync(_Allowed(JobsViews.RunKind, gone), AbortToken);

        allowed.Should().HaveCount(3).And.OnlyContain(answer => answer.DisabledReason == "This run no longer exists");
    }

    private static DashboardAllowedCommandsRequest _Allowed(string kind, Guid id) =>
        new()
        {
            Targets =
            [
                new ObjectRef
                {
                    Module = "jobs",
                    Kind = kind,
                    Id = id.ToString(),
                },
            ],
            User = _User,
        };

    private static DashboardCommandRequest _Request(
        string commandId,
        Guid id,
        IReadOnlyDictionary<string, JsonElement>? arguments = null
    ) =>
        new()
        {
            CommandId = commandId,
            Ids = [id.ToString()],
            Arguments = arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            Channel = DashboardChannels.Ui,
            User = _User,
            Reason = "test",
        };
}
