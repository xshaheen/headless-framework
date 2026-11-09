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
/// The in-memory mirror of the relational lease fencing suite: every write that takes a row from its attempt (a
/// reclaim, a cancel, a delete, an edit, or a sweep) ends that attempt's lease, so the attempt's late completion is
/// refused and the taking write's status stands. Each completion names the attempt's own owner, so only the generation
/// and the ended lease can refuse it.
/// </summary>
public sealed class LeaseFencingProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task same_owner_reclaim_of_an_expired_row_refuses_the_earlier_attempts_completion()
    {
        var (provider, time) = _Create();
        var job = _TimeJob(JobStatus.Idle, lockedUntil: null);
        await provider.AddTimeJobsAsync([job], AbortToken);

        var first = (await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        time.Advance(_Lease.Add(TimeSpan.FromSeconds(1)));
        var second = (await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken))
            .Should()
            .ContainSingle("the node takes back its own expired row")
            .Subject;

        second.Lease.Holder.Should().Be(first.Lease.Holder);
        second.Lease.Generation.Should().BeGreaterThan(first.Lease.Generation);

        (await provider.UpdateTimeJobAsync(_Completion(job.Id, first.Lease.Generation), AbortToken))
            .Should()
            .Be(0, "the reclaim fenced off the earlier attempt");
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Queued);

        (await provider.UpdateTimeJobAsync(_Completion(job.Id, second.Lease.Generation), AbortToken))
            .Should()
            .Be(1, "the current attempt still completes");
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task cancel_of_a_claimed_idle_row_refuses_the_attempts_completion()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Idle, _Now.UtcDateTime.Add(_Lease));
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.RequestTimeJobCancellationAsync(job.Id, AbortToken)).Should().BeTrue();

        await _AssertTimeJobCompletionRefusedAsync(provider, job.Id, JobStatus.Cancelled, RowLeaseState.Revoked);
    }

    [Fact]
    public async Task delete_of_a_running_row_refuses_the_attempts_completion()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _Now.UtcDateTime.Add(_Lease));
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.RemoveTimeJobsAsync([job.Id], AbortToken)).Should().Be(1);

        (await provider.UpdateTimeJobAsync(_Completion(job.Id, generation: 1), AbortToken)).Should().Be(0);
        (await provider.GetTimeJobByIdAsync(job.Id, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task edit_of_a_claimed_queued_row_refuses_the_attempts_completion()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _Now.UtcDateTime.Add(_Lease));
        await provider.AddTimeJobsAsync([job], AbortToken);

        (
            await provider.UpdateTimeJobsAsync(
                [
                    new FakeTimeJob
                    {
                        Id = job.Id,
                        Function = job.Function,
                        Description = "Edited while claimed",
                    },
                ],
                AbortToken
            )
        ).Should().Be(1);

        await _AssertTimeJobCompletionRefusedAsync(provider, job.Id, JobStatus.Idle, RowLeaseState.Revoked);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry, JobStatus.Idle)]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    public async Task dead_node_sweep_refuses_the_swept_attempts_time_job_completion(
        NodeDeathPolicy policy,
        JobStatus swept
    )
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _Now.UtcDateTime.AddMinutes(-1), policy);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.ReleaseDeadNodeTimeJobResourcesAsync(_NodeA, AbortToken)).Should().Be(1);

        await _AssertTimeJobCompletionRefusedAsync(provider, job.Id, swept, expectedLease: null);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry, JobStatus.Idle)]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    public async Task stalled_reclaim_refuses_the_lapsed_attempts_time_job_completion(
        NodeDeathPolicy policy,
        JobStatus swept
    )
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.InProgress, _Now.UtcDateTime.AddMinutes(-1), policy);
        await provider.AddTimeJobsAsync([job], AbortToken);

        (await provider.ReclaimStalledTimeJobsAsync(AbortToken)).Should().Be(1);

        await _AssertTimeJobCompletionRefusedAsync(provider, job.Id, swept, expectedLease: null);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry, JobStatus.Idle)]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    public async Task dead_node_sweep_refuses_the_swept_attempts_occurrence_completion(
        NodeDeathPolicy policy,
        JobStatus swept
    )
    {
        var (provider, _) = _Create();
        var id = await _SeedLapsedOccurrenceAsync(provider, policy);

        (await provider.ReleaseDeadNodeOccurrenceResourcesAsync(_NodeA, AbortToken)).Should().Be(1);

        await _AssertOccurrenceCompletionRefusedAsync(provider, id, swept);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry, JobStatus.Idle)]
    [InlineData(NodeDeathPolicy.MarkFailed, JobStatus.Failed)]
    public async Task stalled_reclaim_refuses_the_lapsed_attempts_occurrence_completion(
        NodeDeathPolicy policy,
        JobStatus swept
    )
    {
        var (provider, _) = _Create();
        var id = await _SeedLapsedOccurrenceAsync(provider, policy);

        (await provider.ReclaimStalledCronJobOccurrencesAsync(AbortToken)).Should().Be(1);

        await _AssertOccurrenceCompletionRefusedAsync(provider, id, swept);
    }

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create()
    {
        var time = new FakeTimeProvider(_Now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = _Lease });
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider()), time);
    }

    private static FakeTimeJob _TimeJob(
        JobStatus status,
        DateTime? lockedUntil,
        NodeDeathPolicy policy = NodeDeathPolicy.Retry
    )
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = status,
            Lease = lockedUntil is { } until ? TestLeases.Held(_NodeA, until) : default,
            OnNodeDeath = policy,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
        };
    }

    private async Task<Guid> _SeedLapsedOccurrenceAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        NodeDeathPolicy policy
    )
    {
        var occurrence = new CronJobOccurrenceEntity<FakeCronJob>
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.InProgress,
            Lease = TestLeases.Held(_NodeA, _Now.UtcDateTime.AddMinutes(-1)),
            OnNodeDeath = policy,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(-2),
            CronJobId = Guid.NewGuid(),
        };
        occurrence.CronJob = new FakeCronJob
        {
            Id = occurrence.CronJobId,
            Function = "fn",
            Expression = "* * * * *",
        };
        await provider.InsertCronJobsAsync([occurrence.CronJob], AbortToken);
        await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        return occurrence.Id;
    }

    private async Task _AssertTimeJobCompletionRefusedAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        Guid id,
        JobStatus taken,
        RowLeaseState? expectedLease
    )
    {
        var afterTaking = (await provider.GetTimeJobByIdAsync(id, AbortToken))!;
        afterTaking.Status.Should().Be(taken);
        afterTaking.Lease.State.Should().NotBe(RowLeaseState.Active, "taking the row ends the attempt's lease");

        if (expectedLease is { } state)
        {
            afterTaking.Lease.State.Should().Be(state);
        }

        (await provider.UpdateTimeJobAsync(_Completion(id, generation: 1), AbortToken)).Should().Be(0);
        (await provider.GetTimeJobByIdAsync(id, AbortToken))!.Status.Should().Be(taken);
    }

    private async Task _AssertOccurrenceCompletionRefusedAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        Guid id,
        JobStatus swept
    )
    {
        var completion = new JobExecutionState
        {
            JobId = id,
            FunctionName = "fn",
            Type = JobType.CronJobOccurrence,
            ClaimOwner = _NodeA,
            ClaimGeneration = 1,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);

        (await provider.UpdateCronJobOccurrenceAsync(completion, AbortToken)).Should().Be(0);
        var row = (await provider.GetAllCronJobOccurrencesAsync(x => x.Id == id, AbortToken))
            .Should()
            .ContainSingle()
            .Subject;
        row.Status.Should().Be(swept);
        row.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    private static JobExecutionState _Completion(Guid id, long generation)
    {
        return new JobExecutionState
        {
            JobId = id,
            FunctionName = "fn",
            Type = JobType.TimeJob,
            ClaimOwner = _NodeA,
            ClaimGeneration = generation,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);
    }
}
