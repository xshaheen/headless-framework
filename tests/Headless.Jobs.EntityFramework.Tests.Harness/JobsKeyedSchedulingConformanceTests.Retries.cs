// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public abstract partial class JobsKeyedSchedulingConformanceTests<TFixture>
{
    protected abstract void ConfigureRetry(DbContextOptionsBuilder options);

    public virtual async Task keyed_operations_support_retry_enabled_contexts()
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        using var host = Fixture.BuildCoordinatedEnqueueHost<JobsDbContext>("keyed-native-retry", ConfigureRetry);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var key = new JobKey("native-retry");
        var created = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate(),
            cancellationToken: AbortToken
        );
        created.Disposition.Should().Be(JobScheduleDisposition.Created);
        var replaced = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate([4]),
            1,
            AbortToken
        );
        replaced.Disposition.Should().Be(JobScheduleDisposition.Replaced);
        replaced.Generation.Should().Be(2);
        var cancelled = await store.CancelKeyedTimeJobAsync(new JobKeyScope("deadline"), key, 2, AbortToken);
        cancelled.Disposition.Should().Be(JobScheduleDisposition.Cancelled);
        var persisted = await store.GetTimeJobByIdAsync(replaced.RunId!.Value, AbortToken);
        persisted!.Status.Should().Be(JobStatus.Cancelled);
        persisted.Generation.Should().Be(2);
        persisted.Request.Should().Equal(4);
    }

    public virtual async Task keyed_retry_restores_candidate_and_custom_properties(bool replace)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new KeyedSaveFailureInterceptor();
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        var key = new JobKey("retry-candidate");
        if (replace)
        {
            await store.ScheduleKeyedTimeJobAsync(key, _RetryCandidate(), cancellationToken: AbortToken);
        }

        var candidate = _RetryCandidate();
        fault.Armed = true;
        var result = await store.ScheduleKeyedTimeJobAsync(key, candidate, replace ? 1 : null, AbortToken);

        result.Disposition.Should().Be(replace ? JobScheduleDisposition.Replaced : JobScheduleDisposition.Created);
        result.Generation.Should().Be(replace ? 2 : 1);
        result.RunId.Should().Be(candidate.Id);
        fault.Attempts.Should().Be(2);
        fault.Contexts.Should().HaveCount(2);
        candidate.BusinessKey.Should().Be(key.Value);
        candidate.Generation.Should().Be(result.Generation);
        candidate.Request.Should().Equal(1, 2, 3);
        candidate.RetryIntervals.Should().Equal(2, 5);
        candidate.CustomLabel.Should().Be("consumer-value");

        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        var stored = await context.Set<RetryTimeJob>().SingleAsync(row => row.Id == result.RunId, AbortToken);
        stored.Request.Should().Equal(1, 2, 3);
        stored.RetryIntervals.Should().Equal(2, 5);
        stored.CustomLabel.Should().Be("consumer-value");
        stored.IntentFingerprint.Should().Be(candidate.IntentFingerprint);
        (await context.Set<RetryTimeJob>().CountAsync(AbortToken)).Should().Be(replace ? 2 : 1);
        (await context.Set<RetryTimeJob>().CountAsync(row => row.IsCurrentGeneration == true, AbortToken))
            .Should()
            .Be(1);
    }

    public virtual async Task keyed_commit_fault_is_not_replayed(string operation, bool afterCommit)
    {
        await Fixture.ResetDatabaseAsync(AbortToken);
        var fault = new KeyedCommitFailureInterceptor(afterCommit);
        using var host = _BuildRetryHost(fault);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<RetryJobsDbContext>(host, AbortToken);
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<RetryTimeJob, CronJobEntity>>();
        var key = new JobKey("commit-fault");
        if (!string.Equals(operation, "schedule", StringComparison.Ordinal))
        {
            await store.ScheduleKeyedTimeJobAsync(key, _RetryCandidate(), cancellationToken: AbortToken);
        }

        var candidate = _RetryCandidate();
        fault.Armed = true;
        Func<Task<JobScheduleResult>> execute = operation switch
        {
            "schedule" => () => store.ScheduleKeyedTimeJobAsync(key, candidate, cancellationToken: AbortToken),
            "replace" => () => store.ScheduleKeyedTimeJobAsync(key, candidate, 1, AbortToken),
            "cancel" => () => store.CancelKeyedTimeJobAsync(new JobKeyScope(candidate.Function), key, 1, AbortToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        await execute.Should().ThrowAsync<KeyedTransientFailureException>();
        fault.Attempts.Should().Be(1);
        candidate.BusinessKey.Should().BeNull();
        candidate.Generation.Should().BeNull();

        await using var context = await host
            .Services.GetRequiredService<IDbContextFactory<RetryJobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        var rows = await context.Set<RetryTimeJob>().ToListAsync(AbortToken);
        var originalRows = string.Equals(operation, "schedule", StringComparison.Ordinal) ? 0 : 1;
        var insertedRows = afterCommit && !string.Equals(operation, "cancel", StringComparison.Ordinal) ? 1 : 0;
        rows.Should().HaveCount(originalRows + insertedRows);
        if (rows.Count != 0)
        {
            var current = rows.Single(row => row.IsCurrentGeneration == true);
            current
                .Generation.Should()
                .Be(afterCommit && string.Equals(operation, "replace", StringComparison.Ordinal) ? 2 : 1);
            current
                .Status.Should()
                .Be(
                    afterCommit && string.Equals(operation, "cancel", StringComparison.Ordinal)
                        ? JobStatus.Cancelled
                        : JobStatus.Idle
                );
        }
    }

    private IHost _BuildRetryHost(IInterceptor interceptor)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCoordination(Fixture.ConfigureCoordination);
        builder.Services.AddHeadlessJobs<RetryTimeJob, CronJobEntity>(options =>
        {
            options.DisableBackgroundServices();
            options.UseEntityFramework(ef =>
                ef.UseJobsDbContext<RetryJobsDbContext>(db =>
                {
                    Fixture.ConfigureStore(db);
                    db.ReplaceService<IExecutionStrategyFactory, KeyedRetryStrategyFactory>()
                        .AddInterceptors(interceptor);
                })
            );
        });
        return builder.Build();
    }

    private static RetryTimeJob _RetryCandidate() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = "deadline",
            ContractVersion = "1",
            ExecutionTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(7),
            Request = [1, 2, 3],
            RetryIntervals = [2, 5],
            CustomLabel = "consumer-value",
        };

    private sealed class RetryTimeJob : TimeJobEntity<RetryTimeJob>
    {
        public string CustomLabel { get; set; } = "";
    }

    private sealed class RetryJobsDbContext(DbContextOptions<RetryJobsDbContext> options)
        : JobsDbContext<RetryTimeJob, CronJobEntity>(options);

    public sealed class KeyedTransientFailureException : Exception;

    private sealed class KeyedRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is KeyedTransientFailureException;
    }

    private sealed class KeyedRetryStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new KeyedRetryStrategy(dependencies);
    }

    private sealed class KeyedSaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Attempts { get; private set; }
        public HashSet<DbContextId> Contexts { get; } = [];

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                Contexts.Add(eventData.Context!.ContextId);
                if (++Attempts == 1)
                {
                    var row = eventData.Context.ChangeTracker.Entries<RetryTimeJob>().Single().Entity;
                    // Mutations after the first durable write must not contaminate the replacement attempt.
                    row.Request![0] = 99;
                    row.RetryIntervals![0] = 99;
                    row.CustomLabel = "failed-attempt";
                    throw new KeyedTransientFailureException();
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class KeyedCommitFailureInterceptor(bool afterCommit) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                Attempts++;
                if (!afterCommit)
                {
                    throw new KeyedTransientFailureException();
                }
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => Armed && afterCommit ? throw new KeyedTransientFailureException() : Task.CompletedTask;
    }
}
