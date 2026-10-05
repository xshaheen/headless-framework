// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// In-memory coverage of stored job progress: the ownership fence on progress writes, the terminal write carrying the
/// throttled final report, progress kept through crash recovery, and a requeue clearing it. The relational providers
/// prove the same contract in the EF conformance harness.
/// </summary>
public sealed class JobProgressProviderTests : TestBase
{
    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTimeOffset _Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task should_store_progress_on_an_owned_running_time_job_stamped_with_the_provider_clock()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);
        fixture.Time.Advance(TimeSpan.FromSeconds(30));

        var written = await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(42.5, "halfway"), AbortToken);

        written.Should().BeTrue();
        var stored = (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.ProgressPercent.Should().Be(42.5);
        stored.ProgressMessage.Should().Be("halfway");
        stored.ProgressUpdatedAt.Should().Be(_Now.AddSeconds(30));
    }

    [Fact]
    public async Task should_replace_the_whole_report_so_a_report_without_a_message_clears_it()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);
        await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(10, "warming up"), AbortToken);

        await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(20), AbortToken);

        var stored = (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.ProgressPercent.Should().Be(20);
        stored.ProgressMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(JobStatus.InProgress, _NodeB)]
    [InlineData(JobStatus.Queued, _NodeA)]
    [InlineData(JobStatus.Succeeded, _NodeA)]
    [InlineData(JobStatus.Idle, null)]
    public async Task should_not_store_progress_on_a_time_job_this_node_does_not_run(JobStatus status, string? owner)
    {
        using var fixture = new Fixture();
        var job = _TimeJob(status, owner);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);

        var written = await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(50), AbortToken);

        written.Should().BeFalse();
        (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.ProgressPercent.Should().BeNull();
    }

    [Fact]
    public async Task should_report_nothing_written_for_an_unknown_time_job()
    {
        using var fixture = new Fixture();

        (await fixture.Provider.UpdateTimeJobProgressAsync(Guid.NewGuid(), new(50), AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_store_progress_on_an_owned_running_cron_occurrence_only()
    {
        using var fixture = new Fixture();
        var definition = fixture.CronJob();
        await fixture.Provider.InsertCronJobsAsync([definition], AbortToken);
        var owned = fixture.Occurrence(definition, JobStatus.InProgress, _NodeA, _Now.UtcDateTime);
        var foreign = fixture.Occurrence(definition, JobStatus.InProgress, _NodeB, _Now.UtcDateTime.AddMinutes(1));
        await fixture.Provider.InsertCronJobOccurrencesAsync([owned, foreign], AbortToken);

        (await fixture.Provider.UpdateCronJobOccurrenceProgressAsync(owned.Id, new(75, "nearly"), AbortToken))
            .Should()
            .BeTrue();
        (await fixture.Provider.UpdateCronJobOccurrenceProgressAsync(foreign.Id, new(75), AbortToken))
            .Should()
            .BeFalse();

        var stored = await fixture.Provider.GetAllCronJobOccurrencesAsync(_ => true, AbortToken);
        var ownedRow = stored.Single(x => x.Id == owned.Id);
        ownedRow.ProgressPercent.Should().Be(75);
        ownedRow.ProgressMessage.Should().Be("nearly");
        ownedRow.ProgressUpdatedAt.Should().Be(_Now);
        stored.Single(x => x.Id == foreign.Id).ProgressPercent.Should().BeNull();
    }

    [Fact]
    public async Task should_write_the_unflushed_report_with_the_terminal_status()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);
        await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(60, "throttled away"), AbortToken);
        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        var state = _State(job.Id, JobType.TimeJob)
            .SetProperty(x => x.Status, JobStatus.Succeeded)
            .SetProperty(x => x.Progress, new JobProgress(100, "done"));

        (await fixture.Provider.UpdateTimeJobAsync(state, AbortToken)).Should().Be(1);

        var stored = (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Succeeded);
        stored.ProgressPercent.Should().Be(100);
        stored.ProgressMessage.Should().Be("done");
        stored.ProgressUpdatedAt.Should().Be(_Now.AddSeconds(5));
    }

    [Fact]
    public async Task should_keep_the_stored_report_on_a_terminal_write_without_one()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);
        await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(35, "stuck here"), AbortToken);
        var state = _State(job.Id, JobType.TimeJob).SetProperty(x => x.Status, JobStatus.Failed);

        await fixture.Provider.UpdateTimeJobAsync(state, AbortToken);

        var stored = (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Failed);
        stored.ProgressPercent.Should().Be(35, "a failed run shows how far it got");
        stored.ProgressMessage.Should().Be("stuck here");
    }

    [Fact]
    public async Task should_write_the_unflushed_report_with_a_cron_occurrence_terminal_status()
    {
        using var fixture = new Fixture();
        var definition = fixture.CronJob();
        await fixture.Provider.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = fixture.Occurrence(definition, JobStatus.InProgress, _NodeA, _Now.UtcDateTime);
        await fixture.Provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        var state = _State(occurrence.Id, JobType.CronJobOccurrence)
            .SetProperty(x => x.Status, JobStatus.Succeeded)
            .SetProperty(x => x.Progress, new JobProgress(100));

        (await fixture.Provider.UpdateCronJobOccurrenceAsync(state, AbortToken)).Should().Be(1);

        var stored = (await fixture.Provider.GetAllCronJobOccurrencesAsync(_ => true, AbortToken)).Single();
        stored.ProgressPercent.Should().Be(100);
        stored.ProgressUpdatedAt.Should().Be(_Now);
    }

    [Theory]
    [InlineData(JobStatus.InProgress, _NodeB)]
    [InlineData(JobStatus.Succeeded, _NodeA)]
    public async Task should_refuse_a_cron_occurrence_completion_this_node_does_not_own(JobStatus status, string owner)
    {
        using var fixture = new Fixture();
        var definition = fixture.CronJob();
        await fixture.Provider.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = fixture.Occurrence(definition, status, owner, _Now.UtcDateTime);
        await fixture.Provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        var state = _State(occurrence.Id, JobType.CronJobOccurrence)
            .SetProperty(x => x.Status, JobStatus.Failed)
            .SetProperty(x => x.Progress, new JobProgress(50));

        // Bounded: the fence must answer 0 rather than spin on its compare-and-swap retry.
        // Task.Run, because the in-memory provider answers synchronously and a spin would otherwise hang the test.
        var affected = await Task.Run(
                () => fixture.Provider.UpdateCronJobOccurrenceAsync(state, AbortToken),
                AbortToken
            )
            .WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        affected.Should().Be(0);
        var stored = (await fixture.Provider.GetAllCronJobOccurrencesAsync(_ => true, AbortToken)).Single();
        stored.Status.Should().Be(status);
        stored.ProgressPercent.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_progress_when_crash_recovery_releases_the_run_for_another_attempt()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);
        await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(80, "before the crash"), AbortToken);
        fixture.Time.Advance(_Lease + TimeSpan.FromMinutes(1));

        (await fixture.Provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);

        var stored = (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(1);
        stored.ProgressPercent.Should().Be(80);
        stored.ProgressMessage.Should().Be("before the crash");
    }

    [Fact]
    public async Task should_clear_progress_when_a_failed_time_job_is_requeued()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);
        await fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(40, "failed here"), AbortToken);
        await fixture.Provider.UpdateTimeJobAsync(
            _State(job.Id, JobType.TimeJob).SetProperty(x => x.Status, JobStatus.Failed),
            AbortToken
        );

        (await fixture.Provider.RequeueTimeJobAsync(job.Id, AbortToken)).Should().Be(JobRequeueOutcome.Requeued);

        var stored = (await fixture.Provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.ProgressPercent.Should().BeNull();
        stored.ProgressMessage.Should().BeNull();
        stored.ProgressUpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task should_clear_progress_when_a_failed_cron_occurrence_is_requeued()
    {
        using var fixture = new Fixture();
        var definition = fixture.CronJob();
        await fixture.Provider.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = fixture.Occurrence(definition, JobStatus.InProgress, _NodeA, _Now.UtcDateTime);
        await fixture.Provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        await fixture.Provider.UpdateCronJobOccurrenceProgressAsync(occurrence.Id, new(40), AbortToken);
        await fixture.Provider.UpdateCronJobOccurrenceAsync(
            _State(occurrence.Id, JobType.CronJobOccurrence).SetProperty(x => x.Status, JobStatus.Failed),
            AbortToken
        );

        (await fixture.Provider.RequeueCronJobOccurrenceAsync(occurrence.Id, AbortToken))
            .Should()
            .Be(JobRequeueOutcome.Requeued);

        var stored = (await fixture.Provider.GetAllCronJobOccurrencesAsync(_ => true, AbortToken)).Single();
        stored.ProgressPercent.Should().BeNull();
        stored.ProgressMessage.Should().BeNull();
        stored.ProgressUpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task should_not_report_a_lost_lease_when_progress_and_renewal_write_the_same_row_concurrently()
    {
        using var fixture = new Fixture();
        var job = _TimeJob(JobStatus.InProgress, _NodeA);
        await fixture.Provider.AddTimeJobsAsync([job], AbortToken);

        // Both writes replace the row by compare-and-swap; a swap lost to the other writer must retry, not read as
        // "not owned", or the renewal loop would cancel a healthy job.
        var renewals = Enumerable
            .Range(0, 200)
            .Select(_ => Task.Run(() => fixture.Provider.RenewTimeJobLeaseAsync(job.Id, AbortToken), AbortToken));
        var progress = Enumerable
            .Range(0, 200)
            .Select(i =>
                Task.Run(
                    () => fixture.Provider.UpdateTimeJobProgressAsync(job.Id, new(i % 101), AbortToken),
                    AbortToken
                )
            );

        (await Task.WhenAll(renewals)).Should().AllBeEquivalentTo(1);
        (await Task.WhenAll(progress)).Should().AllBeEquivalentTo(true);
    }

    private static TimeJobEntity _TimeJob(JobStatus status, string? owner)
    {
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = status,
            OwnerId = owner,
            LockedUntil = owner is null ? null : _Now.UtcDateTime.AddMinutes(1),
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    private static JobExecutionState _State(Guid id, JobType type)
    {
        return new JobExecutionState
        {
            JobId = id,
            FunctionName = "fn",
            Type = type,
        };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;

        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Time);
            services.AddHeadlessGuidGenerator();
            services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = _Lease });
            _services = services.BuildServiceProvider();
            Provider = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(_services);
        }

        public FakeTimeProvider Time { get; } = new(_Now);

        public JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> Provider { get; }

        public CronJobEntity CronJob() =>
            new()
            {
                Id = Guid.NewGuid(),
                Function = "cron-job",
                Expression = "* * * * *",
            };

        public CronJobOccurrenceEntity<CronJobEntity> Occurrence(
            CronJobEntity cron,
            JobStatus status,
            string ownerId,
            DateTime executionTime
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                CronJobId = cron.Id,
                CronJob = cron,
                ExecutionTime = executionTime,
                Status = status,
                OwnerId = ownerId,
                LockedUntil = _Now.UtcDateTime.AddMinutes(1),
            };

        public void Dispose() => _services.Dispose();
    }
}
