// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        await store.ReleaseAcquiredTimeJobsAsync(
            [
                direct
                    .Concat(swept)
                    .First(x => string.Equals(x.Function, TestJobs.BillingCloseDay, StringComparison.Ordinal))
                    .Id,
            ],
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
        (await store.QueueTimedOutTimeJobsAsync(AbortToken).ToArrayAsync(AbortToken)).Should().HaveCount(_Limit);
        var limited = _TimeJob(TestJobs.BillingCloseDay, clock.GetUtcNow().UtcDateTime.AddSeconds(30));
        var unlimited = _TimeJob(TestJobs.OrdersShip, clock.GetUtcNow().UtcDateTime.AddSeconds(60));
        await store.AddTimeJobsAsync([limited, unlimited], AbortToken);

        // when
        var saturatedPeek = await store.GetEarliestTimeJobsAsync(AbortToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.ReleaseAcquiredTimeJobsAsync([], AbortToken);
        var freedPeek = await store.GetEarliestTimeJobsAsync(AbortToken);

        // then
        saturatedPeek.Jobs.Select(x => x.Id).Should().Equal(unlimited.Id);
        freedPeek.Jobs.Select(x => x.Id).Should().Equal(limited.Id);
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
            .And.OnlyContain(x => x.Status == JobStatus.Idle && x.OwnerId == null && x.LockedUntil == null);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);

        // when the instant is overdue and every earlier lease has lapsed, the fallback sweep fills the limit and stops
        clock.Advance(TimeSpan.FromMinutes(31));
        var swept = await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        swept.Should().HaveCount(_Limit);
        (await _LiveLeasesAsync(store, clock)).Should().Be(_Limit);
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

    private static FakeTimeProvider _Clock() => new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static async Task<int> _LiveLeasesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        TimeProvider clock
    )
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var timeJobs = await store.GetTimeJobsAsync(x => x.Function == TestJobs.BillingCloseDay, AbortToken);
        var occurrences = await store.GetAllCronJobOccurrencesAsync(
            x => x.Function == TestJobs.BillingCloseDay,
            AbortToken
        );

        return timeJobs.Count(x => _HoldsSlot(x.Status, x.LockedUntil, now))
            + occurrences.Count(x => _HoldsSlot(x.Status, x.LockedUntil, now));
    }

    private static bool _HoldsSlot(JobStatus status, DateTime? lockedUntil, DateTime now) =>
        status is JobStatus.Queued or JobStatus.InProgress && lockedUntil > now;

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
