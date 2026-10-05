// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public abstract partial class JobsKeyedSchedulingConformanceTests<TFixture>
{
    // A transient fault after a store write already ran inside its transaction must replay the whole transaction on a
    // fresh context: had the first attempt's write survived, the replay's fenced update would match nothing.
    public virtual async Task store_transaction_replays_whole_after_a_transient_fault(bool cron)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new FirstUpdateFailureInterceptor();
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var (job, definition) = await _SeedStoreRetryRowsAsync(host);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        fault.Armed = true;

        if (cron)
        {
            var paused = await store.PauseCronJobAsync(definition.Id, DateTimeOffset.UtcNow, AbortToken);
            paused.Should().NotBeNull();
            paused!.IsPaused.Should().BeTrue();
            paused.ScheduleRevision.Should().Be(definition.ScheduleRevision + 1);
        }
        else
        {
            (await store.RequestTimeJobCancellationAsync(job.Id, AbortToken)).Should().BeTrue();
            (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Cancelled);
        }

        fault.Contexts.Should().HaveCount(2);
    }

    // A fault raised once the commit started is surfaced, never replayed: the commit may already be durable.
    public virtual async Task store_transaction_commit_fault_is_not_replayed(bool afterCommit)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new KeyedCommitFailureInterceptor(afterCommit);
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var (job, _) = await _SeedStoreRetryRowsAsync(host);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        fault.Armed = true;

        var cancel = () => store.RequestTimeJobCancellationAsync(job.Id, AbortToken);

        await cancel.Should().ThrowAsync<KeyedTransientFailureException>();
        fault.Attempts.Should().Be(1);
        fault.Armed = false;
        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!
            .Status.Should()
            .Be(afterCommit ? JobStatus.Cancelled : JobStatus.Idle);
    }

    private static async Task<(RetryTimeJob Job, CronJobEntity Definition)> _SeedStoreRetryRowsAsync(IHost host)
    {
        var job = _RetryCandidate();
        var definition = _OrdinaryRetryDefinition();
        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        context.AddRange(job, definition);
        await context.SaveChangesAsync(AbortToken);

        return (job, definition);
    }

    private sealed class FirstUpdateFailureInterceptor : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public HashSet<DbContextId> Contexts { get; } = [];

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                // Fail only the first attempt, after its UPDATE already ran inside the transaction.
                if (Contexts.Add(eventData.Context!.ContextId) && Contexts.Count == 1)
                {
                    throw new KeyedTransientFailureException();
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
