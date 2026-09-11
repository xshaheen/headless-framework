// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public abstract partial class JobsKeyedSchedulingConformanceTests<TFixture>
{
    public virtual async Task ordinary_write_transactions_support_retry_enabled_contexts()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        using var host = Fixture.BuildCoordinatedEnqueueHost<JobsDbContext>("ordinary-native-retry", ConfigureRetry);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var job = JobsKeyedSchedulingScenarios.Candidate();
        var definition = _OrdinaryRetryDefinition();
        await using (
            var context = await host
                .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                .CreateDbContextAsync(AbortToken)
        )
        {
            context.AddRange(job, definition);
            await context.SaveChangesAsync(AbortToken);
        }

        job.Request = [4, 5];
        (await store.UpdateTimeJobsAsync([job], AbortToken)).Should().Be(1);
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.Request.Should().Equal(4, 5);
        var occurrence = _OrdinaryRetryOccurrence(definition);
        (await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken)).Should().Be(1);
        (await store.GetCronJobOccurrenceRequestAsync(occurrence.Id, AbortToken)).Should().Equal(6, 7);
        occurrence.Function.Should().Be(definition.Function);
        occurrence.Request.Should().Equal(6, 7);
        occurrence.CronJob.Should().BeNull();
    }

    public virtual async Task ordinary_write_retry_uses_fresh_context_and_entities(bool cron)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new OrdinarySaveFailureInterceptor();
        using var host = _BuildRetryHost(fault);
        var (job, occurrence) = await _SeedOrdinaryRetryAsync(host);
        var newChild = _RetryCandidate();
        newChild.Id = Guid.Empty;
        newChild.Request = [4, 5];
        newChild.CustomLabel = "edited-value";
        newChild.TenantId = "original-tenant";
        newChild.CorrelationId = "original-correlation";
        newChild.CausationId = "original-cause";
        job.Children.Add(newChild);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        fault.Armed = true;

        var affected = cron
            ? await store.InsertCronJobOccurrencesAsync([occurrence], AbortToken)
            : await store.UpdateTimeJobsAsync([job], AbortToken);

        affected.Should().Be(cron ? 1 : 3);
        fault.Attempts.Should().Be(2);
        fault.Contexts.Should().HaveCount(2);
        fault.Entities.Should().OnlyHaveUniqueItems();
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        if (cron)
        {
            var stored = await context.Set<CronJobOccurrenceEntity<CronJobEntity>>().SingleAsync(AbortToken);
            stored.Request.Should().Equal(6, 7);
            stored.ExecutionTime.Should().Be(occurrence.ExecutionTime);
            occurrence.Request.Should().Equal(6, 7);
        }
        else
        {
            var rows = await context.Set<RetryTimeJob>().ToListAsync(AbortToken);
            rows.Should().HaveCount(3);
            foreach (var row in rows)
            {
                row.Request.Should().Equal(4, 5);
                row.RetryIntervals.Should().Equal(2, 5);
                row.CustomLabel.Should().Be("edited-value");
                row.TenantId.Should().Be("original-tenant");
                row.CorrelationId.Should().Be("original-correlation");
                row.CausationId.Should().Be("original-cause");
            }
            rows.Where(row => row.Id != job.Id).Should().OnlyContain(row => row.ParentId == job.Id);
            job.Children.Should().OnlyContain(row => row.Request!.SequenceEqual(new byte[] { 4, 5 }));
        }
    }

    public virtual async Task ordinary_write_commit_fault_is_not_replayed(bool cron, bool afterCommit)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new KeyedCommitFailureInterceptor(afterCommit);
        using var host = _BuildRetryHost(fault);
        var (job, occurrence) = await _SeedOrdinaryRetryAsync(host);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        fault.Armed = true;
        Func<Task<int>> execute = cron
            ? () => store.InsertCronJobOccurrencesAsync([occurrence], AbortToken)
            : () => store.UpdateTimeJobsAsync([job], AbortToken);

        await execute.Should().ThrowAsync<KeyedTransientFailureException>();
        fault.Attempts.Should().Be(1);
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        if (cron)
        {
            (await context.Set<CronJobOccurrenceEntity<CronJobEntity>>().CountAsync(AbortToken))
                .Should()
                .Be(afterCommit ? 1 : 0);
            occurrence.Request.Should().BeNull();
            occurrence.CronJob.Should().NotBeNull();
        }
        else
        {
            var rows = await context.Set<RetryTimeJob>().ToListAsync(AbortToken);
            rows.Should().HaveCount(2);
            foreach (var row in rows)
            {
                row.Request.Should().Equal(afterCommit ? [4, 5] : [1, 2, 3]);
                row.CustomLabel.Should().Be(afterCommit ? "edited-value" : "consumer-value");
            }
        }
    }

    private static async Task<(
        RetryTimeJob Job,
        CronJobOccurrenceEntity<CronJobEntity> Occurrence
    )> _SeedOrdinaryRetryAsync(IHost host)
    {
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var job = _RetryCandidate();
        var child = _RetryCandidate();
        job.Children.Add(child);
        var definition = _OrdinaryRetryDefinition();
        foreach (var row in new[] { job, child })
        {
            row.TenantId = "original-tenant";
            row.CorrelationId = "original-correlation";
            row.CausationId = "original-cause";
        }
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        context.AddRange(job, definition);
        await context.SaveChangesAsync(AbortToken);
        foreach (var row in new[] { job, child })
        {
            row.Request = [4, 5];
            row.CustomLabel = "edited-value";
            row.TenantId = "replacement-tenant";
            row.CorrelationId = "replacement-correlation";
            row.CausationId = "replacement-cause";
        }
        return (job, _OrdinaryRetryOccurrence(definition));
    }

    private static CronJobEntity _OrdinaryRetryDefinition() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "ordinary-cron",
            Expression = "0 * * * * *",
            Request = [6, 7],
        };

    private static CronJobOccurrenceEntity<CronJobEntity> _OrdinaryRetryOccurrence(CronJobEntity definition) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            CronJob = definition,
            ExecutionTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    private sealed class OrdinarySaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Attempts { get; private set; }
        public HashSet<DbContextId> Contexts { get; } = [];
        public List<object> Entities { get; } = [];

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
            {
                return ValueTask.FromResult(result);
            }
            var context = eventData.Context!;
            Contexts.Add(context.ContextId);
            Entities.AddRange(context.ChangeTracker.Entries().Select(entry => entry.Entity));
            if (++Attempts == 1)
            {
                // Fail after SQL succeeds so retry must undo the whole transaction and rebuild the graph.
                foreach (var entry in context.ChangeTracker.Entries<RetryTimeJob>())
                {
                    entry.Entity.Request![0] = 99;
                    entry.Entity.RetryIntervals![0] = 99;
                    entry.Entity.CustomLabel = "failed-attempt";
                }
                foreach (var entry in context.ChangeTracker.Entries<CronJobOccurrenceEntity<CronJobEntity>>())
                {
                    entry.Entity.Request![0] = 99;
                    entry.Entity.ExecutionTime = entry.Entity.ExecutionTime.AddHours(1);
                }
                throw new KeyedTransientFailureException();
            }
            return ValueTask.FromResult(result);
        }
    }
}
