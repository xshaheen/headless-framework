// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Cross-provider conformance for the persisted scheduling trace context: the stored <c>TraceParent</c> survives the
/// insert, every claim projection, and an ordinary update; a requeue replaces it; and an occurrence the claim
/// strategy materializes from a definition carries none.
/// </summary>
public abstract class JobsTraceParentConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Scheduled = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string _Edited = "00-11111111111111111111111111111111-2222222222222222-01";
    private const string _Requeued = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    /// <summary>The stored context survives insert, an ordinary update, and the immediate claim.</summary>
    public virtual async Task time_job_trace_parent_survives_insert_update_and_claim()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("trace-parent-time");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = _Persistence(host);
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "trace-parent",
                ExecutionTime = DateTime.UtcNow,
                TraceParent = _Scheduled,
            };
            await persistence.AddTimeJobsAsync([job], ct);
            (await persistence.GetTimeJobByIdAsync(job.Id, ct))!.TraceParent.Should().Be(_Scheduled);

            // An edit is not a new cause, so a different value on the edited entity is ignored.
            var edited = (await persistence.GetTimeJobByIdAsync(job.Id, ct))!;
            edited.Description = "edited";
            edited.TraceParent = _Edited;
            (await persistence.UpdateTimeJobsAsync([edited], ct)).Should().Be(1);
            var stored = (await persistence.GetTimeJobByIdAsync(job.Id, ct))!;
            stored.Description.Should().Be("edited");
            stored.TraceParent.Should().Be(_Scheduled);

            var claimed = (await persistence.AcquireImmediateTimeJobsAsync([job.Id], ct))
                .Should()
                .ContainSingle()
                .Subject;
            claimed.TraceParent.Should().Be(_Scheduled, "the claim projection carries the scheduling context");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A requeue is the new cause: it replaces the stored context on a time job and an occurrence.</summary>
    public virtual async Task requeue_replaces_the_stored_trace_parent()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("trace-parent-requeue");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = _Persistence(host);
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "trace-parent-requeue",
                Status = JobStatus.Failed,
                ExecutionTime = DateTime.UtcNow.AddHours(-1),
                ExceptionMessage = "boom",
                TraceParent = _Scheduled,
            };
            await persistence.AddTimeJobsAsync([job], ct);
            var definition = await _InsertDefinitionAsync(persistence, ct);
            var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = definition.Id,
                ExecutionTime = DateTime.UtcNow.AddHours(-1),
                TraceParent = _Scheduled,
            };
            await persistence.InsertCronJobOccurrencesAsync([occurrence], ct);
            await using (var db = await _DbContextAsync(host, ct))
            {
                await db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                    .Where(x => x.Id == occurrence.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, JobStatus.Failed), ct);
            }

            (await persistence.RequeueTimeJobAsync(job.Id, _Requeued, ct)).Should().Be(JobRequeueOutcome.Requeued);
            (await persistence.RequeueCronJobOccurrenceAsync(occurrence.Id, _Requeued, ct))
                .Should()
                .Be(JobRequeueOutcome.Requeued);

            (await persistence.GetTimeJobByIdAsync(job.Id, ct))!.TraceParent.Should().Be(_Requeued);
            (await persistence.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, ct))
                .Single()
                .TraceParent.Should()
                .Be(_Requeued);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// An on-demand occurrence keeps the context its creator stamped through the immediate claim, while an occurrence
    /// the claim strategy materializes from the definition carries none.
    /// </summary>
    public virtual async Task on_demand_occurrence_keeps_its_trace_parent_and_a_materialized_one_has_none()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("trace-parent-occurrence");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = _Persistence(host);
            var onDemandDefinition = await _InsertDefinitionAsync(persistence, ct);
            var onDemand = new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = onDemandDefinition.Id,
                ExecutionTime = DateTime.UtcNow,
                TraceParent = _Scheduled,
            };
            await persistence.InsertCronJobOccurrencesAsync([onDemand], ct);
            (await persistence.AcquireImmediateCronOccurrencesAsync([onDemand.Id], ct))
                .Should()
                .ContainSingle()
                .Which.TraceParent.Should()
                .Be(_Scheduled);

            var generatedDefinition = await _InsertDefinitionAsync(persistence, ct);
            var executionTime = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var materialized = (
                await persistence
                    .QueueCronJobOccurrencesAsync(
                        (
                            executionTime,
                            [
                                new JobManagerDispatchContext(generatedDefinition.Id)
                                {
                                    FunctionName = generatedDefinition.Function,
                                    Expression = generatedDefinition.Expression,
                                    ScheduleRevision = generatedDefinition.ScheduleRevision,
                                    OnNodeDeath = NodeDeathPolicy.Retry,
                                },
                            ]
                        ),
                        ct
                    )
                    .ToArrayAsync(ct)
            ).Should().ContainSingle().Subject;

            materialized.TraceParent.Should().BeNull("no request caused a generated occurrence");
            (await persistence.GetAllCronJobOccurrencesAsync(x => x.Id == materialized.Id, ct))
                .Single()
                .TraceParent.Should()
                .BeNull();
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static async Task<JobsDbContext> _DbContextAsync(IHost host, CancellationToken ct) =>
        await host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>().CreateDbContextAsync(ct);

    private static async Task<CronJobEntity> _InsertDefinitionAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        CancellationToken ct
    )
    {
        var definition = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "trace-parent-cron",
            Expression = "* * * * *",
        };
        await persistence.InsertCronJobsAsync([definition], ct);
        return definition;
    }
}
