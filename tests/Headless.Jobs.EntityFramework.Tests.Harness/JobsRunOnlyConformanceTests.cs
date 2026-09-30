// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// A host that limits what it runs with <c>RunOnly</c> never leases a row of a job it filtered out, on any relational
/// claim path, and a host without the filter picks that row up.
/// </summary>
public abstract class JobsRunOnlyConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Runnable = JobsCoordinationFixtureExtensions.CoordinatedFunctionName;
    private const string _Filtered = JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName;

    public virtual async Task filtered_host_never_claims_a_filtered_time_job_that_an_unfiltered_host_claims(
        bool useNativeClaims
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var filteredHost = fixture.BuildHost(
            "run-only-api",
            useNativeClaims: useNativeClaims,
            configureJobs: jobs => jobs.RunOnly(_Runnable)
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(filteredHost, ct);
        await filteredHost.StartAsync(ct);
        using var workerHost = fixture.BuildHost("run-only-worker", useNativeClaims: useNativeClaims);
        await workerHost.StartAsync(ct);

        try
        {
            var filtered = _Store(filteredHost);
            var overdueRunnable = _TimeJob(_Runnable, DateTime.UtcNow.AddMinutes(-2));
            var overdueFiltered = _TimeJob(_Filtered, DateTime.UtcNow.AddMinutes(-2));
            var dueRunnable = _TimeJob(_Runnable, DateTime.UtcNow.AddMinutes(10));
            var dueFiltered = _TimeJob(_Filtered, DateTime.UtcNow.AddMinutes(5));
            await filtered.AddTimeJobsAsync([overdueRunnable, overdueFiltered, dueRunnable, dueFiltered], ct);

            // The peek skips the earlier filtered row, so the direct claim never sees it as a candidate.
            var peeked = await filtered.GetEarliestTimeJobsAsync(ct);
            peeked.Jobs.Select(x => x.Id).Should().Equal(dueRunnable.Id);

            // Every other path must refuse the filtered row even when a caller hands it over directly.
            var directlyClaimed = await filtered
                .QueueTimeJobsAsync([(await filtered.GetTimeJobByIdAsync(dueFiltered.Id, ct))!], ct)
                .ToArrayAsync(ct);
            directlyClaimed.Should().BeEmpty();
            (await filtered.AcquireImmediateTimeJobsAsync([dueFiltered.Id], ct)).Should().BeEmpty();
            var swept = await filtered.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            swept.Select(x => x.Id).Should().Equal(overdueRunnable.Id);

            var untouched = (await filtered.GetTimeJobByIdAsync(overdueFiltered.Id, ct))!;
            untouched.Status.Should().Be(JobStatus.Idle);
            untouched.OwnerId.Should().BeNull();

            var workerSwept = await _Store(workerHost).QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            workerSwept.Select(x => x.Id).Should().Equal(overdueFiltered.Id);
        }
        finally
        {
            await workerHost.StopAsync(ct);
            await filteredHost.StopAsync(ct);
        }
    }

    public virtual async Task filtered_host_schedules_a_filtered_job_that_an_unfiltered_host_claims()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var filteredHost = fixture.BuildHost("run-only-schedule", configureJobs: jobs => jobs.RunOnly(_Runnable));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(filteredHost, ct);
        await filteredHost.StartAsync(ct);
        using var workerHost = fixture.BuildHost("run-only-schedule-worker");
        await workerHost.StartAsync(ct);

        try
        {
            var id = await filteredHost
                .Services.GetRequiredService<IJobScheduler>()
                .ScheduleAsync(
                    new CoordinatedFacadeRequest(Guid.NewGuid(), "handed-off"),
                    DateTimeOffset.UtcNow.AddSeconds(-30),
                    cancellationToken: ct
                );

            (await _Store(filteredHost).QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct)).Should().BeEmpty();
            var claimed = await _Store(workerHost).QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            claimed.Should().ContainSingle().Which.Id.Should().Be(id);
            claimed[0].Function.Should().Be(_Filtered);
        }
        finally
        {
            await workerHost.StopAsync(ct);
            await filteredHost.StopAsync(ct);
        }
    }

    public virtual async Task filtered_host_never_claims_or_dispatches_a_filtered_cron_job(bool useNativeClaims)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "run-only-cron",
            useNativeClaims: useNativeClaims,
            configureJobs: jobs => jobs.RunOnly(_Runnable)
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var store = _Store(host);
            var runnableDefinition = _CronDefinition(_Runnable);
            var filteredDefinition = _CronDefinition(_Filtered);
            await store.InsertCronJobsAsync([runnableDefinition, filteredDefinition], ct);
            var overdue = DateTime.UtcNow.AddMinutes(-2);
            var runnableOccurrence = _Occurrence(runnableDefinition.Id, overdue);
            var filteredOccurrence = _Occurrence(filteredDefinition.Id, overdue);
            await store.InsertCronJobOccurrencesAsync([runnableOccurrence, filteredOccurrence], ct);

            var swept = await store.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            swept.Select(x => x.Id).Should().Equal(runnableOccurrence.Id);

            // A dispatch wave naming both definitions leases only the runnable one's next occurrence.
            var instant = DateTime.UtcNow.AddMinutes(30);
            var claimed = await store
                .QueueCronJobOccurrencesAsync(
                    (instant, [_Dispatch(runnableDefinition), _Dispatch(filteredDefinition)]),
                    ct
                )
                .ToArrayAsync(ct);
            claimed.Should().ContainSingle().Which.CronJobId.Should().Be(runnableDefinition.Id);

            var candidates = await store.GetEarliestCronDispatchCandidatesAsync(10, cancellationToken: ct);
            candidates?.Candidates.Should().OnlyContain(x => x.FunctionName == _Runnable);

            var stored = (await store.GetAllCronJobOccurrencesAsync(x => x.Id == filteredOccurrence.Id, ct)).Single();
            stored.Status.Should().Be(JobStatus.Idle);
            stored.OwnerId.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
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

    private static CronJobEntity _CronDefinition(string function) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 0 * * * *",
        };

    private static CronJobOccurrenceEntity<CronJobEntity> _Occurrence(Guid cronJobId, DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = cronJobId,
            ExecutionTime = executionTime,
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
