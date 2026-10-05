// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A job's cluster-wide concurrency limit holds across nodes sharing one store: every claim path counts the job's live
/// leased rows across time jobs and cron occurrences and leases no more than the free slots, and a crashed node's rows
/// stop counting once their leases lapse.
/// </summary>
public abstract class JobsClusterConcurrencyConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Limited = JobsCoordinationFixtureExtensions.CoordinatedFunctionName;
    private const string _Unlimited = JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName;
    private const string _CrashedNode = "cluster-crashed-node";
    private const int _Limit = 2;

    public virtual async Task three_nodes_never_lease_a_limited_job_past_its_cluster_limit()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-a", "cluster-b", "cluster-c"], ct);

        try
        {
            var store = _Store(hosts[0]);
            var overdue = DateTime.UtcNow.AddMinutes(-2);
            TimeJobEntity[] limited = [.. Enumerable.Range(0, 8).Select(_ => _TimeJob(_Limited, overdue))];
            TimeJobEntity[] unlimited = [.. Enumerable.Range(0, 3).Select(_ => _TimeJob(_Unlimited, overdue))];
            await store.AddTimeJobsAsync([.. limited, .. unlimited], ct);

            // Every node races both claim paths at once, several times over, so a count-then-claim race would show.
            for (var round = 0; round < 4; round++)
            {
                await Task.WhenAll(hosts.Select(host => _ClaimEverythingAsync(_Store(host), ct)));
                (await _LiveLeasesAsync(store, _Limited, ct)).Should().Be(_Limit);
            }

            (await _LiveLeasesAsync(store, _Unlimited, ct)).Should().Be(unlimited.Length);

            // Releasing one run frees exactly one slot, and the next race fills it and no more.
            var held = (await store.GetTimeJobsAsync(x => x.Function == _Limited, ct)).First(x =>
                x.Status == JobStatus.Queued
            );
            // Only the owning node's release matches the row, so asking every node releases it exactly once.
            foreach (var host in hosts)
            {
                await _Store(host).ReleaseAcquiredTimeJobsAsync([held.Id], ct);
            }

            (await _LiveLeasesAsync(store, _Limited, ct)).Should().Be(_Limit - 1);

            await Task.WhenAll(hosts.Select(host => _ClaimEverythingAsync(_Store(host), ct)));
            (await _LiveLeasesAsync(store, _Limited, ct)).Should().Be(_Limit);
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    public virtual async Task crashed_node_runs_stop_counting_once_their_leases_lapse()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-survivor"], ct);

        try
        {
            var store = _Store(hosts[0]);
            await fixture.SeedTimeJobAsync(
                Guid.NewGuid(),
                _Limited,
                (int)JobStatus.InProgress,
                _CrashedNode,
                ct,
                lockedUntil: DateTime.UtcNow.AddMinutes(5)
            );
            await fixture.SeedTimeJobAsync(
                Guid.NewGuid(),
                _Limited,
                (int)JobStatus.Queued,
                _CrashedNode,
                ct,
                lockedUntil: DateTime.UtcNow.AddMinutes(5)
            );
            var overdue = DateTime.UtcNow.AddMinutes(-2);
            await store.AddTimeJobsAsync([.. Enumerable.Range(0, 3).Select(_ => _TimeJob(_Limited, overdue))], ct);

            // The crashed node's live leases fill both slots.
            (await store.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct))
                .Should()
                .BeEmpty();

            await _ExpireLeasesAsync(_CrashedNode, ct);

            var claimed = await store.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            claimed.Should().HaveCount(_Limit).And.OnlyContain(x => x.Function == _Limited);
            (await _LiveLeasesAsync(store, _Limited, ct)).Should().Be(_Limit);
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    public virtual async Task scheduler_peek_skips_a_limited_job_with_no_free_slot()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-peek"], ct);

        try
        {
            var store = _Store(hosts[0]);
            for (var i = 0; i < _Limit; i++)
            {
                await fixture.SeedTimeJobAsync(
                    Guid.NewGuid(),
                    _Limited,
                    (int)JobStatus.InProgress,
                    _CrashedNode,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(5)
                );
            }

            var limited = _TimeJob(_Limited, DateTime.UtcNow.AddMinutes(5));
            var unlimited = _TimeJob(_Unlimited, DateTime.UtcNow.AddMinutes(10));
            await store.AddTimeJobsAsync([limited, unlimited], ct);

            // A peeked row the claim cannot lease would hold the scheduler's wake at its due time and spin the loop.
            (await store.GetEarliestTimeJobsAsync(ct))
                .Jobs.Select(x => x.Id)
                .Should()
                .Equal(unlimited.Id);

            await _ExpireLeasesAsync(_CrashedNode, ct);

            (await store.GetEarliestTimeJobsAsync(ct)).Jobs.Select(x => x.Id).Should().Equal(limited.Id);
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    public virtual async Task cron_occurrences_share_the_limit_with_time_jobs()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-cron"], ct);

        try
        {
            var store = _Store(hosts[0]);
            await store.AddTimeJobsAsync([_TimeJob(_Limited, DateTime.UtcNow.AddMinutes(-2))], ct);
            (await store.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct)).Should().ContainSingle();

            CronJobEntity[] definitions = [_CronDefinition(), _CronDefinition(), _CronDefinition()];
            await store.InsertCronJobsAsync(definitions, ct);
            var instant = DateTime.UtcNow.AddMinutes(30);

            // One slot is left after the leased time job: one occurrence is leased, and the others are materialized
            // unleased for the fallback sweep instead of being dropped.
            var leased = await store
                .QueueCronJobOccurrencesAsync((instant, [.. definitions.Select(_Dispatch)]), ct)
                .ToArrayAsync(ct);
            leased.Should().ContainSingle();

            var occurrences = await store.GetAllCronJobOccurrencesAsync(x => x.Function == _Limited, ct);
            occurrences.Should().HaveCount(definitions.Length);
            occurrences
                .Where(x => x.Id != leased[0].Id)
                .Should()
                .OnlyContain(x => x.Status == JobStatus.Idle && x.OwnerId == null && x.LockedUntil == null);
            (await _LiveLeasesAsync(store, _Limited, ct)).Should().Be(_Limit);
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    public virtual async Task fallback_sweeps_on_two_nodes_lease_overdue_occurrences_up_to_the_limit()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-sweep-a", "cluster-sweep-b"], ct);

        try
        {
            var store = _Store(hosts[0]);
            CronJobEntity[] definitions = [.. Enumerable.Range(0, 4).Select(_ => _CronDefinition())];
            await store.InsertCronJobsAsync(definitions, ct);
            var overdue = DateTime.UtcNow.AddMinutes(-2);
            await store.InsertCronJobOccurrencesAsync(
                [
                    .. definitions.Select(definition => new CronJobOccurrenceEntity<CronJobEntity>
                    {
                        Id = Guid.NewGuid(),
                        CronJobId = definition.Id,
                        ExecutionTime = overdue,
                    }),
                ],
                ct
            );

            var swept = await Task.WhenAll(
                hosts.Select(host => _Store(host).QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct).AsTask())
            );

            swept.Sum(x => x.Length).Should().Be(_Limit);
            (await _LiveLeasesAsync(store, _Limited, ct)).Should().Be(_Limit);
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    public virtual async Task immediate_acquire_leaves_a_limited_job_to_the_scheduler_claim()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-immediate"], ct);

        try
        {
            var store = _Store(hosts[0]);
            var limited = _TimeJob(_Limited, DateTime.UtcNow.AddMinutes(-2));
            var unlimited = _TimeJob(_Unlimited, DateTime.UtcNow.AddMinutes(-2));
            await store.AddTimeJobsAsync([limited, unlimited], ct);

            var acquired = await store.AcquireImmediateTimeJobsAsync([limited.Id, unlimited.Id], ct);
            acquired.Select(x => x.Id).Should().Equal(unlimited.Id);
            (await fixture.ReadTimeJobAsync(limited.Id, ct)).Should().Be(((int)JobStatus.Idle, (string?)null));

            var claimed = await store.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            claimed.Select(x => x.Id).Should().Equal(limited.Id);
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    public virtual async Task optimistic_claim_fallback_refuses_a_cluster_limit()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var hosts = await _StartHostsAsync(["cluster-cas"], ct, useNativeClaims: false);

        try
        {
            var act = async () => await _Store(hosts[0]).QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            await act.Should().ThrowAsync<NotSupportedException>().WithMessage($"*{_Limited}*");
        }
        finally
        {
            await _StopHostsAsync(hosts, ct);
        }
    }

    private async Task<IHost[]> _StartHostsAsync(
        string[] nodeIds,
        CancellationToken cancellationToken,
        bool useNativeClaims = true
    )
    {
        var hosts = nodeIds
            .Select(nodeId =>
                fixture.BuildHost(
                    nodeId,
                    useNativeClaims: useNativeClaims,
                    configureJobs: jobs => jobs.Tune(_Limited, job => job.ClusterConcurrency(_Limit))
                )
            )
            .ToArray();
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(hosts[0], cancellationToken);
        foreach (var host in hosts)
        {
            await host.StartAsync(cancellationToken);
        }

        return hosts;
    }

    private static async Task _StopHostsAsync(IHost[] hosts, CancellationToken cancellationToken)
    {
        foreach (var host in hosts)
        {
            await host.StopAsync(cancellationToken);
            host.Dispose();
        }
    }

    private static async Task _ClaimEverythingAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        CancellationToken cancellationToken
    )
    {
        var peeked = await store.GetEarliestTimeJobsAsync(cancellationToken);
        await Task.WhenAll(
            store.QueueTimeJobsAsync(peeked.Jobs, cancellationToken).ToArrayAsync(cancellationToken).AsTask(),
            store.QueueTimedOutTimeJobsAsync(cancellationToken).ToArrayAsync(cancellationToken).AsTask()
        );
    }

    private static async Task<int> _LiveLeasesAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        string function,
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var timeJobs = await store.GetTimeJobsAsync(x => x.Function == function, cancellationToken);
        var occurrences = await store.GetAllCronJobOccurrencesAsync(x => x.Function == function, cancellationToken);

        return timeJobs.Count(x => _HoldsSlot(x.Status, x.LockedUntil, now))
            + occurrences.Count(x => _HoldsSlot(x.Status, x.LockedUntil, now));
    }

    private static bool _HoldsSlot(JobStatus status, DateTime? lockedUntil, DateTime now) =>
        status is JobStatus.Queued or JobStatus.InProgress && lockedUntil > now;

    private async Task _ExpireLeasesAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"LockedUntil\" = @past WHERE \"OwnerId\" = @owner;"
        );
        JobsCoordinationFixtureExtensions.AddParameter(command, "@past", DateTime.UtcNow.AddMinutes(-1));
        JobsCoordinationFixtureExtensions.AddParameter(command, "@owner", ownerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

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
            Function = _Limited,
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
}
