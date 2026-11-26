// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Registration;

/// <summary>
/// In-memory storage enforces a job's cluster-wide limit on every claim path: it never leases more live runs of the
/// job than the limit, across time jobs and cron occurrences, and a lapsed lease stops counting.
/// </summary>
public sealed class JobsClusterConcurrencyTests : TestBase
{
    private const int _Limit = 2;

    [Fact]
    public async Task should_lease_no_more_than_the_limit_and_refill_a_released_slot()
    {
        // given
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        TimeJobEntity[] limited = [.. Enumerable.Range(0, 5).Select(_ => _TimeJob(TestJobs.BillingCloseDay, overdue))];
        var unlimited = _TimeJob(TestJobs.OrdersShip, overdue);
        await store.AddTimeJobsAsync([.. limited, unlimited], AbortToken);

        // when
        var peeked = await store.GetEarliestTimeJobsAsync(AbortToken);
        var direct = await store.QueueTimeJobsAsync(peeked.Jobs, AbortToken).ToArrayAsync(AbortToken);
        var swept = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        direct
            .Concat(swept)
            .Count(x => string.Equals(x.Function, TestJobs.BillingCloseDay, StringComparison.Ordinal))
            .Should()
            .Be(_Limit);
        direct.Concat(swept).Should().Contain(x => x.Id == unlimited.Id);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);

        // when a run is released, exactly one more is leased
        var released = direct
            .Concat(swept)
            .First(x => string.Equals(x.Function, TestJobs.BillingCloseDay, StringComparison.Ordinal));
        await store.ReleaseAcquiredTimeJobsAsync(
            [JobsLeaseTables.TimeJob(released.Id, released.Lease.Generation)],
            released.Lease.Holder!,
            AbortToken
        );
        var refilled = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        refilled.Should().ContainSingle().Which.Function.Should().Be(TestJobs.BillingCloseDay);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_stop_counting_a_run_once_its_lease_lapses()
    {
        // given
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        await store.AddTimeJobsAsync(
            [.. Enumerable.Range(0, 4).Select(_ => _TimeJob(TestJobs.BillingCloseDay, overdue))],
            AbortToken
        );
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().HaveCount(_Limit);
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().BeEmpty();

        // when the leases lapse, as they do when the owning node dies
        clock.Advance(TimeSpan.FromMinutes(6));
        var reclaimed = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        reclaimed.Should().HaveCount(_Limit);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_not_peek_a_limited_job_until_a_slot_frees()
    {
        // given
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        await store.AddTimeJobsAsync(
            [.. Enumerable.Range(0, _Limit).Select(_ => _TimeJob(TestJobs.BillingCloseDay, overdue))],
            AbortToken
        );
        var saturating = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);
        saturating.Should().HaveCount(_Limit);
        var limited = _TimeJob(TestJobs.BillingCloseDay, clock.GetUtcNow().UtcDateTime.AddSeconds(30));
        var unlimited = _TimeJob(TestJobs.OrdersShip, clock.GetUtcNow().UtcDateTime.AddSeconds(60));
        await store.AddTimeJobsAsync([limited, unlimited], AbortToken);

        // when
        var saturatedPeek = await store.GetEarliestTimeJobsAsync(AbortToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.ReleaseAcquiredTimeJobsAsync([], saturating[0].Lease.Holder!, AbortToken);
        var freedPeek = await store.GetEarliestTimeJobsAsync(AbortToken);

        // then
        saturatedPeek.Jobs.Select(x => x.Id).Should().Equal(unlimited.Id);
        freedPeek.Jobs.Select(x => x.Id).Should().Equal(limited.Id);
    }

    [Fact]
    public async Task should_claim_overdue_time_jobs_behind_a_full_page_of_saturated_jobs()
    {
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var older = clock.GetUtcNow().UtcDateTime.AddMinutes(-3);
        await store.AddTimeJobsAsync(
            [.. Enumerable.Range(0, 100 + _Limit).Select(_ => _TimeJob(TestJobs.BillingCloseDay, older))],
            AbortToken
        );
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().HaveCount(_Limit);
        var unlimited = _TimeJob(TestJobs.OrdersShip, older.AddMinutes(1));
        await store.AddTimeJobsAsync([unlimited], AbortToken);

        var claimed = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        claimed.Select(x => x.Id).Should().Equal(unlimited.Id);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_claim_overdue_cron_occurrences_behind_a_full_page_of_saturated_occurrences()
    {
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var older = clock.GetUtcNow().UtcDateTime.AddMinutes(-3);
        await store.AddTimeJobsAsync(
            [.. Enumerable.Range(0, _Limit).Select(_ => _TimeJob(TestJobs.BillingCloseDay, older))],
            AbortToken
        );
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().HaveCount(_Limit);
        var limited = _CronDefinition();
        var unlimited = _CronDefinition();
        unlimited.Function = TestJobs.OrdersShip;
        await store.InsertCronJobsAsync([limited, unlimited], AbortToken);
        var expected = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = unlimited.Id,
            ExecutionTime = older.AddMinutes(1),
        };
        await store.InsertCronJobOccurrencesAsync(
            [
                .. Enumerable
                    .Range(0, 100)
                    .Select(index => new CronJobOccurrenceEntity<CronJobEntity>
                    {
                        Id = Guid.NewGuid(),
                        CronJobId = limited.Id,
                        ExecutionTime = older.AddTicks(index),
                    }),
                expected,
            ],
            AbortToken
        );

        var claimed = await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);

        claimed.Select(x => x.Id).Should().Equal(expected.Id);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_share_the_limit_between_time_jobs_and_cron_occurrences()
    {
        // given
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        await store.AddTimeJobsAsync(
            [_TimeJob(TestJobs.BillingCloseDay, clock.GetUtcNow().UtcDateTime.AddMinutes(-2))],
            AbortToken
        );
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().ContainSingle();
        CronJobEntity[] definitions = [_CronDefinition(), _CronDefinition(), _CronDefinition()];
        await store.InsertCronJobsAsync(definitions, AbortToken);
        var instant = clock.GetUtcNow().UtcDateTime.AddMinutes(30);

        // when
        var leased = await store
            .QueueCronJobOccurrencesAsync((instant, [.. definitions.Select(_Dispatch)]), AbortToken)
            .ToArrayAsync(AbortToken);

        // then the one free slot is leased, and the rest wait unleased for the fallback sweep
        leased.Should().ContainSingle();
        var waiting = (await store.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken)).Where(x =>
            x.Id != leased[0].Id
        );
        waiting
            .Should()
            .HaveCount(definitions.Length - 1)
            .And.OnlyContain(x => x.Status == JobStatus.Idle && x.Lease.State != RowLeaseState.Active);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);

        // when the instant is overdue and every earlier lease has lapsed, the fallback sweep fills the limit and stops
        clock.Advance(TimeSpan.FromMinutes(31));
        var swept = await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        swept.Should().HaveCount(_Limit);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_lease_only_one_of_concurrent_time_and_cron_claims_for_the_last_slot()
    {
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        await store.AddTimeJobsAsync([_TimeJob(TestJobs.BillingCloseDay, overdue)], AbortToken);
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().ContainSingle();
        await store.AddTimeJobsAsync([_TimeJob(TestJobs.BillingCloseDay, overdue)], AbortToken);
        var definition = _CronDefinition();
        await store.InsertCronJobsAsync([definition], AbortToken);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeClaim = Task.Run(
            async () =>
            {
                await start.Task.WaitAsync(AbortToken);
                return await store.QueueTimedOutTimeJobsAsync(AbortToken).CountAsync(AbortToken);
            },
            AbortToken
        );
        var cronClaim = Task.Run(
            async () =>
            {
                await start.Task.WaitAsync(AbortToken);
                return await store
                    .QueueCronJobOccurrencesAsync((clock.GetUtcNow().UtcDateTime, [_Dispatch(definition)]), AbortToken)
                    .CountAsync(AbortToken);
            },
            AbortToken
        );
        start.SetResult();

        (await Task.WhenAll(timeClaim, cronClaim)).Sum().Should().Be(1);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_count_a_renewed_lease_past_its_original_deadline_and_free_the_slot_on_removal()
    {
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        await store.AddTimeJobsAsync(
            [.. Enumerable.Range(0, 4).Select(_ => _TimeJob(TestJobs.BillingCloseDay, overdue))],
            AbortToken
        );
        var leased = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);
        leased.Should().HaveCount(_Limit);
        var running = leased[0];
        var start = new JobExecutionState
        {
            FunctionName = running.Function,
            JobId = running.Id,
            ClaimOwner = running.Lease.Holder!,
            ClaimGeneration = running.Lease.Generation,
        }.SetProperty(x => x.Status, JobStatus.InProgress);
        (await store.UpdateTimeJobAsync(start, AbortToken)).Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(4));
        (await store.RenewTimeJobLeaseAsync(start.ClaimedRow!, start.ClaimOwner!, AbortToken)).Should().Be(1);
        clock.Advance(TimeSpan.FromMinutes(2));
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().ContainSingle();
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);

        (await store.RemoveTimeJobsAsync([running.Id], AbortToken)).Should().Be(1);
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().ContainSingle();
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
    }

    [Fact]
    public async Task should_count_a_chain_descendant_only_once_it_starts_running()
    {
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var now = clock.GetUtcNow().UtcDateTime;
        var overdue = now.AddMinutes(-2);
        var parent = _TimeJob(TestJobs.OrdersShip, now);
        var child = new TimeJobEntity { Id = Guid.NewGuid(), Function = TestJobs.BillingCloseDay };
        parent.Children.Add(child);
        await store.AddTimeJobsAsync(
            [parent, _TimeJob(TestJobs.BillingCloseDay, overdue), _TimeJob(TestJobs.BillingCloseDay, overdue)],
            AbortToken
        );
        (await store.AcquireImmediateTimeJobsAsync([parent.Id], AbortToken)).Should().ContainSingle();
        var leased = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);
        leased.Should().HaveCount(_Limit);
        var released = leased[0];
        await store.ReleaseAcquiredTimeJobsAsync(
            [JobsLeaseTables.TimeJob(released.Id, released.Lease.Generation)],
            released.Lease.Holder!,
            AbortToken
        );

        var storedChild = (await store.GetTimeJobByIdAsync(child.Id, AbortToken))!;
        var start = new JobExecutionState
        {
            FunctionName = child.Function,
            JobId = child.Id,
            ClaimOwner = storedChild.Lease.Holder!,
            ClaimGeneration = storedChild.Lease.Generation,
        }.SetProperty(x => x.Status, JobStatus.InProgress);
        (await store.UpdateTimeJobAsync(start, AbortToken)).Should().Be(1);

        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().BeEmpty();
        (await store.GetTimeJobByIdAsync(released.Id, AbortToken))!.Status.Should().Be(JobStatus.Idle);
    }

    [Fact]
    public async Task should_leave_a_limited_job_to_the_scheduler_claim_instead_of_acquiring_it_immediately()
    {
        // given
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var due = clock.GetUtcNow().UtcDateTime;
        var limited = _TimeJob(TestJobs.BillingCloseDay, due);
        var unlimited = _TimeJob(TestJobs.OrdersShip, due);
        await store.AddTimeJobsAsync([limited, unlimited], AbortToken);
        var definition = _CronDefinition();
        await store.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            ExecutionTime = due,
        };
        await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken);

        // when
        var acquired = await store.AcquireImmediateTimeJobsAsync([limited.Id, unlimited.Id], AbortToken);
        var acquiredOccurrences = await store.AcquireImmediateCronOccurrencesAsync([occurrence.Id], AbortToken);

        // then
        acquired.Select(x => x.Id).Should().Equal(unlimited.Id);
        acquiredOccurrences.Should().BeEmpty();
        (await store.GetTimeJobByIdAsync(limited.Id, AbortToken))!.Status.Should().Be(JobStatus.Idle);
    }

    [Fact]
    public async Task should_hold_no_slot_for_a_limited_job_that_is_not_due_after_every_claim_path()
    {
        // given
        var clock = _Clock();
        await using var provider = _Provider(clock);
        var store = _Store(provider);
        var future = _TimeJob(TestJobs.BillingCloseDay, clock.GetUtcNow().UtcDateTime.AddMinutes(10));
        await store.AddTimeJobsAsync([future], AbortToken);

        // when the scheduler polls, and the peek, direct, and timed-out claims all run
        var (_, functions) = await provider.GetRequiredService<IInternalJobManager>().GetNextJobs(AbortToken);
        var peeked = await store.GetEarliestTimeJobsAsync(AbortToken);
        var direct = await store.QueueTimeJobsAsync(peeked.Jobs, AbortToken).ToArrayAsync(AbortToken);
        var swept = await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        functions.Should().BeEmpty();
        direct.Concat(swept).Should().BeEmpty();
        (await _LiveLeasesAsync(store, clock)).Should().Be(0);
        var stored = await store.GetTimeJobByIdAsync(future.Id, AbortToken);
        stored!.Status.Should().Be(JobStatus.Idle);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);
    }

    private static FakeTimeProvider _Clock() => new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static async Task<int> _LiveLeasesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        TimeProvider clock
    )
    {
        var now = clock.GetUtcNow();
        var timeJobs = await store.GetTimeJobsAsync(x => x.Function == TestJobs.BillingCloseDay, AbortToken);
        var occurrences = await store.GetAllCronJobOccurrencesAsync(
            x => x.Function == TestJobs.BillingCloseDay,
            AbortToken
        );

        return timeJobs.Count(x => _HoldsSlot(x.Status, x.Lease, now))
            + occurrences.Count(x => _HoldsSlot(x.Status, x.Lease, now));
    }

    private static bool _HoldsSlot(JobStatus status, RowLeaseSnapshot lease, DateTimeOffset now) =>
        status is JobStatus.Queued or JobStatus.InProgress
        && lease.State == RowLeaseState.Active
        && lease.ExpiresAt > now;

    private static TimeJobEntity _TimeJob(string function, DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            ExecutionTime = executionTime,
        };

    private static CronJobEntity _CronDefinition() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = TestJobs.BillingCloseDay,
            Expression = "0 0 * * * *",
        };

    private static JobManagerDispatchContext _Dispatch(CronJobEntity definition) =>
        new(definition.Id)
        {
            FunctionName = definition.Function,
            Expression = definition.Expression,
            ScheduleRevision = definition.ScheduleRevision,
            OnNodeDeath = NodeDeathPolicy.Retry,
        };

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IServiceProvider provider) =>
        provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static ServiceProvider _Provider(TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<BillingJobsModule>().AddModule<OrdersJobsModule>();
            jobs.Tune(TestJobs.BillingCloseDay, job => job.ClusterConcurrency(_Limit));
        });
        return services.BuildServiceProvider();
    }
}
