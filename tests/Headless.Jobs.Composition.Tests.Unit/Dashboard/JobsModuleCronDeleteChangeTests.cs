// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Dashboard;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// What a cron delete from the dashboard tells open views: the cron job is gone, and so is every occurrence it stored,
/// announced as one change that names the cron job however many occurrences went.
/// </summary>
public sealed class JobsModuleCronDeleteChangeTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity("test"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_publish_one_removal_of_the_cron_jobs_occurrences_when_it_is_deleted()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob();
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);
        await _AddOccurrencesAsync(node.Store, cronJob.Id, JobStatus.Succeeded, 3);
        await _AddOccurrencesAsync(node.Store, cronJob.Id, JobStatus.Failed, 2);

        // when
        var result = await node.Module.ExecuteAsync(
            new DashboardCommandRequest
            {
                CommandId = "jobs.delete-cron",
                Ids = [cronJob.Id.ToString()],
                Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Channel = DashboardChannels.Ui,
                User = _User,
                Reason = "test",
            },
            AbortToken
        );

        // then: the cron job's removal, and one removal of its occurrences that names the cron job, not each row
        result.Rows.Should().ContainSingle().Which.Outcome.Should().Be(DashboardRowOutcome.Done);
        var changes = node.Published.For(cronJob.Id);
        changes.Should().Contain(change => change.Target.Kind == "cron-job" && change.Projection == null);
        var occurrences = changes.Should().ContainSingle(change => change.Target.Kind == "cron-occurrences").Subject;
        occurrences.Target.Module.Should().Be("jobs");
        occurrences.Projection.Should().BeNull();
        occurrences.AffectsAttention.Should().BeTrue("a removed failed occurrence leaves Needs you");
        node.Published.Changes.Should().HaveCount(2, "no change is published per removed occurrence");
    }

    [Fact]
    public async Task should_publish_no_occurrence_removal_when_the_cron_delete_is_refused()
    {
        // given
        await using var node = JobsModuleNode.Create(_time);
        var cronJob = JobsModuleNode.CronJob();
        await node.Store.InsertCronJobsAsync([cronJob], AbortToken);
        await _AddOccurrencesAsync(node.Store, cronJob.Id, JobStatus.InProgress, 1);

        // when
        var result = await node.Module.ExecuteAsync(
            new DashboardCommandRequest
            {
                CommandId = "jobs.delete-cron",
                Ids = [cronJob.Id.ToString()],
                Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Channel = DashboardChannels.Ui,
                User = _User,
                Reason = "test",
            },
            AbortToken
        );

        // then
        result.Refusal.Should().NotBeNull();
        node.Published.For(cronJob.Id).Should().BeEmpty();
    }

    private async Task _AddOccurrencesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid cronJobId,
        JobStatus status,
        int count
    )
    {
        var now = _time.GetUtcNow();
        var occurrences = Enumerable
            .Range(0, count)
            .Select(index => new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJobId,
                Status = status,
                ExecutionTime = now.UtcDateTime.AddHours(-index - 1),
                ExecutedAt = status is JobStatus.Succeeded or JobStatus.Failed ? now.AddHours(-index - 1) : null,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1),
            })
            .ToArray();

        (await store.InsertCronJobOccurrencesAsync(occurrences, AbortToken)).Should().Be(count);
    }
}
