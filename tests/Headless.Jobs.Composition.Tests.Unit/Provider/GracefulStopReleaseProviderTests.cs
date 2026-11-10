// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store's graceful-stop releases: a started row goes back uncharged, an interrupted row is settled by
/// its node-death policy uncharged, and neither touches a row a peer already reclaimed. The relational providers run
/// the same scenarios in the coordination conformance suite.
/// </summary>
public sealed class GracefulStopReleaseProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "node-a@1";
    private const string _Peer = "node-b@1";
    private const string _Reason = "interrupted by host shutdown";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);

    // The generation this node's claim drew. A peer that reclaimed the row drew the next one, so this node's writes
    // carry a stale generation for it.
    private const long _ClaimGeneration = 1;

    [Fact]
    public async Task should_release_a_started_time_job_to_idle_without_charging_it()
    {
        // given
        var provider = _Create();
        var job = _TimeJob(JobStatus.InProgress, _Owner, NodeDeathPolicy.MarkFailed, retryCount: 1);
        await provider.AddTimeJobsAsync([job], AbortToken);

        // when
        var affected = await provider.ReleaseInProgressTimeJobAsync(_Claimed(job), AbortToken);

        // then: the release ignores the policy, because the job never ran
        affected.Should().Be(1);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Idle);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.RetryCount.Should().Be(1);
        stored.ExecutionTime.Should().Be(job.ExecutionTime, "the release keeps the row due at its stored instant");
    }

    [Theory]
    [InlineData(JobStatus.Queued, _Owner)]
    [InlineData(JobStatus.Idle, _Owner)]
    [InlineData(JobStatus.Succeeded, _Owner)]
    [InlineData(JobStatus.InProgress, _Peer)]
    [InlineData(JobStatus.InProgress, null)]
    public async Task should_not_release_a_row_that_is_not_running_under_the_claim_owner(
        JobStatus status,
        string? ownerId
    )
    {
        // given: not started, finished, or already reclaimed by a peer
        var provider = _Create();
        var job = _TimeJob(status, ownerId, NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        // when
        var released = await provider.ReleaseInProgressTimeJobAsync(_Claimed(job), AbortToken);
        var settled = await provider.SettleInterruptedTimeJobAsync(_Claimed(job), _Reason, null, AbortToken);

        // then
        released.Should().Be(0);
        settled.Should().Be(0);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(status);
        stored.Lease.Holder.Should().Be(ownerId);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(0)]
    public async Task should_settle_an_interrupted_retry_time_job_to_idle_at_the_store_clock_plus_the_remaining_delay(
        int remainingSeconds
    )
    {
        // given: the store's clock has moved on since the job was claimed
        var clock = new FakeTimeProvider(_Now);
        var provider = _Create(clock);
        var job = _TimeJob(JobStatus.InProgress, _Owner, NodeDeathPolicy.Retry, retryCount: 1);
        await provider.AddTimeJobsAsync([job], AbortToken);
        clock.Advance(TimeSpan.FromMinutes(3));

        // when
        var affected = await provider.SettleInterruptedTimeJobAsync(
            _Claimed(job),
            _Reason,
            TimeSpan.FromSeconds(remainingSeconds),
            AbortToken
        );

        // then: due at the store's instant at the settle plus the delay still owed
        affected.Should().Be(1);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Idle);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.RetryCount.Should().Be(1, "a graceful stop is not an attempt the job used up");
        stored.ExecutionTime.Should().Be(new DateTime(2026, 06, 17, 12, 03, remainingSeconds, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public async Task should_reschedule_a_failed_retry_to_idle_at_the_store_clock_plus_the_delay_under_any_policy(
        NodeDeathPolicy policy
    )
    {
        // given: the store's clock has moved on since the job was claimed
        var clock = new FakeTimeProvider(_Now);
        var provider = _Create(clock);
        var job = _TimeJob(JobStatus.InProgress, _Owner, policy, retryCount: 1);
        await provider.AddTimeJobsAsync([job], AbortToken);
        clock.Advance(TimeSpan.FromMinutes(3));

        // when
        var affected = await provider.RescheduleTimeJobRetryAsync(
            _Claimed(job),
            2,
            TimeSpan.FromSeconds(60),
            AbortToken
        );

        // then: the failed attempt is charged and the retry is due a delay after the store's instant
        affected.Should().Be(1);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Idle);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.RetryCount.Should().Be(2);
        stored.ExecutionTime.Should().Be(new DateTime(2026, 06, 17, 12, 04, 00, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(JobStatus.Queued, _Owner)]
    [InlineData(JobStatus.Idle, _Owner)]
    [InlineData(JobStatus.Failed, _Owner)]
    [InlineData(JobStatus.InProgress, _Peer)]
    public async Task should_not_reschedule_a_row_that_is_not_running_under_the_claim_owner(
        JobStatus status,
        string ownerId
    )
    {
        // given
        var provider = _Create();
        var job = _TimeJob(status, ownerId, NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        // when
        var affected = await provider.RescheduleTimeJobRetryAsync(
            _Claimed(job),
            1,
            TimeSpan.FromSeconds(60),
            AbortToken
        );

        // then
        affected.Should().Be(0);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(status);
        stored.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task should_settle_an_interrupted_retry_time_job_at_its_stored_instant_when_no_delay_is_owed()
    {
        // given
        var provider = _Create();
        var job = _TimeJob(JobStatus.InProgress, _Owner, NodeDeathPolicy.Retry);
        await provider.AddTimeJobsAsync([job], AbortToken);

        // when
        var affected = await provider.SettleInterruptedTimeJobAsync(_Claimed(job), _Reason, null, AbortToken);

        // then
        affected.Should().Be(1);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Idle);
        stored.ExecutionTime.Should().Be(job.ExecutionTime);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    [InlineData(NodeDeathPolicy.Skip, JobStatus.Skipped)]
    public async Task should_settle_an_interrupted_time_job_by_its_terminal_policy(
        NodeDeathPolicy policy,
        JobStatus expected
    )
    {
        // given
        var provider = _Create();
        var job = _TimeJob(JobStatus.InProgress, _Owner, policy);
        await provider.AddTimeJobsAsync([job], AbortToken);

        // when
        var affected = await provider.SettleInterruptedTimeJobAsync(
            _Claimed(job),
            _Reason,
            TimeSpan.FromMinutes(1),
            AbortToken
        );

        // then: a terminal settlement keeps the owner and ignores the retry instant
        affected.Should().Be(1);
        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(expected);
        stored.Lease.Holder.Should().Be(_Owner);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.RetryCount.Should().Be(0);
        stored.ExecutionTime.Should().Be(job.ExecutionTime);
        (expected == JobStatus.Failed ? stored.ExceptionMessage : stored.SkippedReason).Should().Be(_Reason);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry, JobStatus.Idle)]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    [InlineData(NodeDeathPolicy.Skip, JobStatus.Skipped)]
    public async Task should_settle_an_interrupted_cron_occurrence_by_its_policy(
        NodeDeathPolicy policy,
        JobStatus expected
    )
    {
        // given
        var provider = _Create();
        var occurrence = await _AddOccurrenceAsync(provider, JobStatus.InProgress, _Owner, policy);

        // when
        var affected = await provider.SettleInterruptedCronJobOccurrenceAsync(
            _Claimed(occurrence),
            _Reason,
            AbortToken
        );

        // then
        affected.Should().Be(1);
        var stored = await _OccurrenceAsync(provider, occurrence.Id);
        stored.Status.Should().Be(expected);
        stored.RetryCount.Should().Be(1);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.ExecutionTime.Should().Be(occurrence.ExecutionTime);
        stored.Lease.Holder.Should().Be(_Owner, "an ended lease keeps the holder that last held it");

        if (expected == JobStatus.Failed)
        {
            stored.ExceptionMessage.Should().Be(_Reason);
        }
        else if (expected == JobStatus.Skipped)
        {
            stored.SkippedReason.Should().Be(_Reason);
            stored.Disposition.Should().Be(CronOccurrenceDisposition.Accounted);
        }
    }

    [Fact]
    public async Task should_release_a_started_cron_occurrence_to_idle_without_charging_it()
    {
        // given
        var provider = _Create();
        var occurrence = await _AddOccurrenceAsync(provider, JobStatus.InProgress, _Owner, NodeDeathPolicy.Skip);

        // when
        var affected = await provider.ReleaseInProgressCronJobOccurrenceAsync(_Claimed(occurrence), AbortToken);

        // then
        affected.Should().Be(1);
        var stored = await _OccurrenceAsync(provider, occurrence.Id);
        stored.Status.Should().Be(JobStatus.Idle);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
        stored.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task should_not_settle_a_cron_occurrence_a_peer_reclaimed()
    {
        // given
        var provider = _Create();
        var occurrence = await _AddOccurrenceAsync(provider, JobStatus.InProgress, _Peer, NodeDeathPolicy.Retry);

        // when
        var released = await provider.ReleaseInProgressCronJobOccurrenceAsync(_Claimed(occurrence), AbortToken);
        var settled = await provider.SettleInterruptedCronJobOccurrenceAsync(_Claimed(occurrence), _Reason, AbortToken);

        // then
        released.Should().Be(0);
        settled.Should().Be(0);
        var stored = await _OccurrenceAsync(provider, occurrence.Id);
        stored.Status.Should().Be(JobStatus.InProgress);
        stored.Lease.Holder.Should().Be(_Peer);
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create(FakeTimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(_Now));
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(
            new SchedulerOptionsBuilder { NodeId = "node-a", LeaseDuration = TimeSpan.FromMinutes(5) }
        );
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }

    private static FakeTimeJob _TimeJob(JobStatus status, string? owner, NodeDeathPolicy policy, int retryCount = 0)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = status,
            // An unowned row was never granted, so it carries no lease at all rather than a holderless live one.
            Lease = TestLeases.Held(owner, owner is null ? null : _Now.AddMinutes(5), _GenerationHeldBy(owner)),
            OnNodeDeath = policy,
            RetryCount = retryCount,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    private async Task<CronJobOccurrenceEntity<FakeCronJob>> _AddOccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        JobStatus status,
        string owner,
        NodeDeathPolicy policy
    )
    {
        var cronJob = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Expression = "* * * * *",
        };
        var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = status,
            Lease = TestLeases.Held(owner, _Now.AddMinutes(5), _GenerationHeldBy(owner)),
            OnNodeDeath = policy,
            RetryCount = 1,
            // Not the default, so a Skip settlement visibly marks the instant accounted for.
            Disposition = CronOccurrenceDisposition.ReplacementOwed,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-1),
            CronJobId = cronJob.Id,
            CronJob = cronJob,
        };
        await provider.InsertCronJobsAsync([cronJob], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        return occurrence;
    }

    private static async Task<CronJobOccurrenceEntity<FakeCronJob>> _OccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        Guid id
    )
    {
        var all = await provider.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken);
        return all.Single(x => x.Id == id);
    }

    private static long _GenerationHeldBy(string? owner) =>
        string.Equals(owner, _Peer, StringComparison.Ordinal) ? _ClaimGeneration + 1 : _ClaimGeneration;

    private static FencedRow<Guid> _Claimed(FakeTimeJob job) => JobsLeaseTables.TimeJob(job.Id, _ClaimGeneration);

    private static FencedRow<Guid> _Claimed(CronJobOccurrenceEntity<FakeCronJob> occurrence) =>
        JobsLeaseTables.CronOccurrence(occurrence.Id, _ClaimGeneration);
}
