// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

// These writes act on entities the caller passes in: the definitions to insert, the edited definition, and the
// occurrences the caller's factory builds. Each test fails the first attempt after its SQL already ran inside the
// transaction, so a replay that kept the first attempt's rows, or reused its mutated entities, shows up as a duplicate,
// a double revision bump, or a lost fence.
public abstract partial class JobsKeyedSchedulingConformanceTests<TFixture>
{
    public virtual async Task seeded_cron_insert_replays_whole_after_a_transient_fault()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new FirstSaveFailureInterceptor();
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        var definitions = new[] { _ReplayDefinition("replay-insert-a"), _ReplayDefinition("replay-insert-b") };
        var anchors = new List<DateTime>();
        fault.Armed = true;

        var result = await store.InsertCronJobsAsync(
            definitions,
            (_, storeUtcNow) =>
            {
                anchors.Add(storeUtcNow);
                return new CronSchedulePositionSeed
                {
                    ReconciledThroughUtc = storeUtcNow,
                    NextDueUtc = storeUtcNow.AddMinutes(1),
                };
            },
            AbortToken
        );

        fault.Contexts.Should().HaveCount(2);
        anchors.Should().HaveCount(4, "each attempt seeds both definitions from its own store anchor");
        result.AffectedRows.Should().Be(2);
        result.StoreUtcNow.Should().Be(anchors[^1]);
        var anchor = result.StoreUtcNow!.Value;

        await using var context = await _CreateRetryContextAsync(host);
        var rows = await context.Set<CronJobEntity>().AsNoTracking().ToListAsync(AbortToken);
        rows.Should().HaveCount(2);
        foreach (var definition in definitions)
        {
            definition.ReconciledThroughUtc.Should().Be(anchor);
            var row = rows.Single(x => x.Id == definition.Id);
            row.ReconciledThroughUtc.Should().BeCloseTo(anchor, TimeSpan.FromMicroseconds(1));
            row.NextDueUtc.Should().BeCloseTo(anchor.AddMinutes(1), TimeSpan.FromMicroseconds(1));
        }

        _ShouldAttachUnchanged(context, definitions);
    }

    public virtual async Task cron_resume_replays_whole_after_a_transient_fault()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new FirstSaveFailureInterceptor();
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        var definition = _ReplayDefinition("replay-resume");
        definition.IsPaused = true;
        definition.ScheduleRevision = 3;
        await _SeedReplayRowsAsync(host, definition);
        var built = new List<CronJobOccurrenceEntity<CronJobEntity>>();
        fault.Armed = true;

        var resumed = await store.ResumeCronJobAsync(
            definition.Id,
            expectedScheduleRevision: 3,
            anchor => _BuildReplayOccurrence(definition, anchor.AddMinutes(1), built),
            DateTimeOffset.UtcNow,
            AbortToken
        );

        fault.Contexts.Should().HaveCount(2);
        built.Should().HaveCount(2, "each attempt builds its occurrence from its own anchor");
        resumed.Should().NotBeNull();
        resumed!.IsPaused.Should().BeFalse();
        resumed.ScheduleRevision.Should().Be(4);

        await using var context = await _CreateRetryContextAsync(host);
        var occurrence = await context
            .Set<CronJobOccurrenceEntity<CronJobEntity>>()
            .AsNoTracking()
            .SingleAsync(AbortToken);
        occurrence.Id.Should().Be(built[^1].Id);
        occurrence.ExecutionTime.Should().BeCloseTo(resumed.NextDueUtc, TimeSpan.FromMicroseconds(1));
        _ShouldAttachUnchanged(context, [definition]);
    }

    public virtual async Task cron_schedule_edit_replays_whole_after_a_transient_fault()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new FirstSaveFailureInterceptor();
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        var stored = _ReplayDefinition("replay-edit");
        stored.ScheduleRevision = 5;
        var pending = _BuildReplayOccurrence(stored, new DateTime(2030, 1, 1, 0, 1, 0, DateTimeKind.Utc), []);
        pending.SnapshotContract(stored);
        pending.CronJob = null!;
        await _SeedReplayRowsAsync(host, stored, pending);

        // The caller edits its own copy, as the manager does, and the provider writes the result back onto it.
        var edited = _ReplayDefinition("replay-edit");
        edited.Id = stored.Id;
        edited.Expression = "0 */10 * * * *";
        edited.ScheduleRevision = 5;
        var built = new List<CronJobOccurrenceEntity<CronJobEntity>>();
        fault.Armed = true;

        var result = await store.UpdateCronJobsAtomicallyAsync(
            [
                new CronJobAtomicUpdate<CronJobEntity>(
                    edited,
                    ExpectedScheduleRevision: 5,
                    anchor => _BuildReplayOccurrence(edited, anchor.AddMinutes(10), built)
                ),
            ],
            DateTimeOffset.UtcNow,
            AbortToken
        );

        fault.Contexts.Should().HaveCount(2);
        built.Should().HaveCount(2);
        result.Should().NotBeNull();
        result![0].Should().BeSameAs(edited);
        edited.ScheduleRevision.Should().Be(6, "the replay bumps the revision once, from the rolled-back state");

        await using var context = await _CreateRetryContextAsync(host);
        var row = await context.Set<CronJobEntity>().AsNoTracking().SingleAsync(AbortToken);
        row.Expression.Should().Be("0 */10 * * * *");
        row.ScheduleRevision.Should().Be(6);
        row.NextDueUtc.Should().BeCloseTo(edited.NextDueUtc, TimeSpan.FromMicroseconds(1));

        var occurrences = await context
            .Set<CronJobOccurrenceEntity<CronJobEntity>>()
            .AsNoTracking()
            .ToListAsync(AbortToken);
        occurrences.Should().HaveCount(2, "the pending occurrence is superseded once and one replacement is written");
        occurrences.Single(x => x.Id == pending.Id).Status.Should().Be(JobStatus.Skipped);
        var replacement = occurrences.Single(x => x.Id != pending.Id);
        replacement.Id.Should().Be(built[^1].Id);
        replacement.Status.Should().Be(JobStatus.Idle);
        _ShouldAttachUnchanged(context, [edited]);
    }

    private static CronJobEntity _ReplayDefinition(string function) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 * * * * *",
            Request = [6, 7],
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

    private static CronJobOccurrenceEntity<CronJobEntity> _BuildReplayOccurrence(
        CronJobEntity definition,
        DateTime executionTime,
        List<CronJobOccurrenceEntity<CronJobEntity>> built
    )
    {
        // A fresh instance per call, carrying the caller's definition, as the manager's occurrence factory builds it.
        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            CronJob = definition,
            Status = JobStatus.Idle,
            ExecutionTime = executionTime,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        built.Add(occurrence);
        return occurrence;
    }

    private static async Task _SeedReplayRowsAsync(IHost host, params object[] rows)
    {
        await using var context = await _CreateRetryContextAsync(host);
        context.AddRange(rows);
        await context.SaveChangesAsync(AbortToken);
    }

    private static Task<RetryJobsDbContext> _CreateRetryContextAsync(IHost host) =>
        host.Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>().CreateDbContextAsync(AbortToken);

    // No live context still tracks the caller's entities: a fresh context attaches them as unchanged.
    private static void _ShouldAttachUnchanged(DbContext context, IEnumerable<CronJobEntity> definitions)
    {
        context.AttachRange(definitions);
        context
            .ChangeTracker.Entries<CronJobEntity>()
            .Should()
            .OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    private sealed class FirstSaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public HashSet<DbContextId> Contexts { get; } = [];

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            // Fail only the first attempt, after its rows were written inside the transaction.
            if (Armed && Contexts.Add(eventData.Context!.ContextId) && Contexts.Count == 1)
            {
                throw new KeyedTransientFailureException();
            }

            return ValueTask.FromResult(result);
        }
    }
}
