// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public abstract partial class JobsTransactionalKeyedConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly DateTimeOffset _Due = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public virtual Task keyed_and_ordinary_writes_share_the_outer_commit_or_rollback(bool commit) =>
        _WithHostAsync(async host =>
        {
            var key = new JobKey("atomic-matrix");
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var operation = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, ct) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                        var created = await _ScheduleAsync(host, key, "first", ct);
                        created.Disposition.Should().Be(JobScheduleDisposition.Created);
                        created.IsProvisional.Should().BeTrue();
                        (await _ScheduleAsync(host, key, "first", ct))
                            .Disposition.Should()
                            .Be(JobScheduleDisposition.Existing);
                        (await _ScheduleAsync(host, key, "different", ct))
                            .Disposition.Should()
                            .Be(JobScheduleDisposition.Conflict);
                        var replaced = await _ScheduleAsync(host, key, "next", ct, generation: 1);
                        replaced.Disposition.Should().Be(JobScheduleDisposition.Replaced);
                        replaced.Generation.Should().Be(2);
                        (
                            await scheduler.CancelKeyedAsync(
                                new JobKeyScope(JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName),
                                key,
                                2,
                                requireAtomicEnlistment: true,
                                ct
                            )
                        )
                            .Disposition.Should()
                            .Be(JobScheduleDisposition.Cancelled);
                        await scheduler.EnqueueAsync(
                            new CoordinatedFacadeRequest(Guid.Empty, "ordinary"),
                            new EnqueueOptions { RequireAtomicEnlistment = true },
                            ct
                        );
                        // Conflict is an ordinary disposition: subsequent caller SQL must still succeed.
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                        if (!commit)
                        {
                            throw new InjectedFailure();
                        }
                    },
                    AbortToken
                );
            if (commit)
            {
                await operation();
            }
            else
            {
                await operation.Should().ThrowAsync<InjectedFailure>();
            }
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(commit ? 2 : 0);
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(commit ? 3 : 0);
            if (commit)
            {
                var observed = await _ScheduleAsync(host, key, "next", AbortToken, required: false);
                observed.IsProvisional.Should().BeFalse();
                observed.Disposition.Should().Be(JobScheduleDisposition.Existing);
                observed.State.Should().Be(JobStatus.Cancelled);
            }
        });

    public virtual Task disposing_uncommitted_scope_discards_business_and_keyed_rows() =>
        _WithHostAsync(async host =>
        {
            await using (var context = await _ContextAsync(host))
            await using (var transaction = await context.Database.BeginTransactionAsync(AbortToken))
            await using (context.Database.EnlistCommitCoordination(transaction, host.Services, AbortToken))
            {
                await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(
                    context.Database.GetDbConnection(),
                    transaction.GetDbTransaction(),
                    AbortToken
                );
                (await _ScheduleAsync(host, new JobKey("dispose"), "first", AbortToken))
                    .IsProvisional.Should()
                    .BeTrue();
            }
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(0);
        });

    public virtual Task replacement_savepoint_restores_superseded_generation_on_insert_failure()
    {
        var fault = new InsertFailureInterceptor();
        return _WithHostAsync(
            async host =>
            {
                var key = new JobKey("savepoint");
                var first = await _ScheduleAsync(host, key, "first", AbortToken, required: false);
                await fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, ct) =>
                    {
                        fault.FailNextKeyedSave = true;
                        var replace = () => _ScheduleAsync(host, key, "next", ct, generation: 1);
                        await replace.Should().ThrowAsync<InjectedFailure>();
                        (await _ScheduleAsync(host, key, "first", ct)).RunId.Should().Be(first.RunId);
                        // The failed insert must not leave generation 1 historical inside a still-usable caller transaction.
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                    },
                    AbortToken
                );
                fault.Failures.Should().Be(1);
                (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(1);
                (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
                var retained = await host
                    .Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
                    .GetTimeJobByIdAsync(first.RunId!.Value, AbortToken);
                retained!.IsCurrentGeneration.Should().BeTrue();
                retained.Status.Should().Be(JobStatus.Idle);
            },
            fault
        );
    }

    public virtual Task commit_boundary_failure_is_not_replayed_and_rows_match_durable_outcome(bool afterCommit)
    {
        var fault = new CommitFailureInterceptor(afterCommit);
        return _WithHostAsync(
            async host =>
            {
                var calls = 0;
                await using var context = await _ContextAsync(host);
                fault.Enabled = true;
                var operation = async () =>
                    await context.ExecuteCoordinatedTransactionAsync(
                        async (caller, ct) =>
                        {
                            calls++;
                            await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(
                                caller.Database.GetDbConnection(),
                                caller.Database.CurrentTransaction!.GetDbTransaction(),
                                ct
                            );
                            await _ScheduleAsync(host, new JobKey("commit-fault"), "first", ct);
                        },
                        host.Services,
                        cancellationToken: AbortToken
                    );
                await operation.Should().ThrowAsync<InjectedFailure>();
                fault.Enabled = false;
                calls.Should().Be(1);
                (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(afterCommit ? 1 : 0);
                (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(afterCommit ? 1 : 0);
                if (afterCommit)
                {
                    (await _ScheduleAsync(host, new JobKey("commit-fault"), "first", AbortToken, required: false))
                        .Disposition.Should()
                        .Be(JobScheduleDisposition.Existing);
                }
            },
            fault
        );
    }

    public virtual Task ef_execution_strategy_retries_known_rollback_with_fresh_units_of_work() =>
        _WithHostAsync(async host =>
        {
            await using var strategyContext = await _ContextAsync(host);
            var strategy = new KnownRollbackStrategy(strategyContext);
            var attempts = 0;
            var contexts = new HashSet<DbContextId>();
            await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _ContextAsync(host);
                contexts.Add(context.ContextId);
                await context.ExecuteCoordinatedTransactionAsync(
                    async (caller, ct) =>
                    {
                        attempts++;
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(
                            caller.Database.GetDbConnection(),
                            caller.Database.CurrentTransaction!.GetDbTransaction(),
                            ct
                        );
                        await _ScheduleAsync(host, new JobKey("known-retry"), "first", ct);
                        if (attempts == 1)
                        {
                            throw new InjectedFailure();
                        }
                    },
                    host.Services,
                    cancellationToken: AbortToken
                );
            });
            attempts.Should().Be(2);
            contexts.Should().HaveCount(2);
            (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(1);
            (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
            var observed = await _ScheduleAsync(host, new JobKey("known-retry"), "first", AbortToken, required: false);
            observed.Generation.Should().Be(1);
            observed.Disposition.Should().Be(JobScheduleDisposition.Existing);
        });

    public virtual Task failure_before_keyed_write_rolls_back_application_state()
    {
        var fault = new SavepointFailureInterceptor();
        return _WithHostAsync(
            async host =>
            {
                fault.FailCreation = true;
                var operation = () =>
                    fixture.RunCoordinatedTransactionAsync(
                        host.Services,
                        async (connection, transaction, ct) =>
                        {
                            await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                            await _ScheduleAsync(host, new JobKey("before-write"), "first", ct);
                        },
                        AbortToken
                    );
                await operation.Should().ThrowAsync<InjectedFailure>();
                fault.Failures.Should().Be(1);
                (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(0);
                (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
            },
            fault
        );
    }

    public virtual Task failed_savepoint_restoration_requires_outer_rollback()
    {
        var insertFailure = new InsertFailureInterceptor();
        var rollbackFailure = new SavepointFailureInterceptor();
        return _WithHostAsync(
            async host =>
            {
                var key = new JobKey("rollback-required");
                var first = await _ScheduleAsync(host, key, "first", AbortToken, required: false);
                var operation = () =>
                    fixture.RunCoordinatedTransactionAsync(
                        host.Services,
                        async (connection, transaction, ct) =>
                        {
                            await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, ct);
                            insertFailure.FailNextKeyedSave = true;
                            rollbackFailure.FailRestoration = true;
                            // Propagate the rollback-required failure to the owner; only its outer rollback restores this unit.
                            await _ScheduleAsync(host, key, "next", ct, generation: 1);
                        },
                        AbortToken
                    );
                var failure = await operation
                    .Should()
                    .ThrowAsync<InvalidOperationException>()
                    .WithMessage("*outer rollback and fresh unit of work are required*");
                failure
                    .Which.InnerException.Should()
                    .BeOfType<AggregateException>()
                    .Which.InnerExceptions.Should()
                    .HaveCount(2);
                insertFailure.Failures.Should().Be(1);
                rollbackFailure.Failures.Should().Be(1);
                (await fixture.CountTimeJobsAsync(AbortToken)).Should().Be(1);
                (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
                var retained = await host
                    .Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
                    .GetTimeJobByIdAsync(first.RunId!.Value, AbortToken);
                retained!.IsCurrentGeneration.Should().BeTrue();
                retained.Status.Should().Be(JobStatus.Idle);
            },
            insertFailure,
            configureServices: services => services.AddSingleton<IInterceptor>(rollbackFailure)
        );
    }

    private async Task _WithHostAsync(
        Func<IHost, Task> body,
        IInterceptor? interceptor = null,
        TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        await fixture.ResetDatabaseAsync(AbortToken);
        IServiceProvider? services = null;
        using var host = fixture.BuildCoordinatedEnqueueHost<JobsDbContext>(
            "transactional-keyed",
            options =>
            {
                options.AddInterceptors(services!.GetServices<IInterceptor>());
                if (interceptor is not null)
                {
                    options.AddInterceptors(interceptor);
                }
            },
            timeProvider: clock,
            configureServices: registrations =>
            {
                registrations.AddEntityFrameworkCommitCoordination();
                configureServices?.Invoke(registrations);
            }
        );
        services = host.Services;
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, AbortToken);
        await fixture.CreateProbeTableAsync(AbortToken);
        await host.StartAsync(AbortToken);
        try
        {
            await body(host);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    private Task<JobsDbContext> _ContextAsync(IHost host) =>
        host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>().CreateDbContextAsync(AbortToken);

    private static Task<JobScheduleResult> _ScheduleAsync(
        IHost host,
        JobKey key,
        string payload,
        CancellationToken ct,
        long? generation = null,
        bool required = true,
        DateTimeOffset? due = null
    )
    {
        var scheduler = host.Services.GetRequiredService<IJobScheduler>();
        var request = new CoordinatedFacadeRequest(Guid.Empty, payload);
        var options = new EnqueueOptions { RequireAtomicEnlistment = required };
        return generation is { } observed
            ? scheduler.ReplaceKeyedAsync(key, observed, request, due ?? _Due, options, ct)
            : scheduler.ScheduleKeyedAsync(key, request, due ?? _Due, options, ct);
    }

    public sealed class InjectedFailure : Exception;

    private sealed class InsertFailureInterceptor : SaveChangesInterceptor
    {
        public bool FailNextKeyedSave { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                FailNextKeyedSave
                && eventData.Context!.ChangeTracker.Entries<TimeJobEntity>().Any(row => row.Entity.BusinessKey != null)
            )
            {
                FailNextKeyedSave = false;
                Failures++;
                throw new InjectedFailure();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitFailureInterceptor(bool afterCommit) : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Enabled && !afterCommit)
            {
                throw new InjectedFailure();
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => Enabled && afterCommit ? throw new InjectedFailure() : Task.CompletedTask;
    }

    private sealed class SavepointFailureInterceptor : DbTransactionInterceptor
    {
        public bool FailCreation { get; set; }
        public bool FailRestoration { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult> CreatingSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (FailCreation)
            {
                FailCreation = false;
                Failures++;
                throw new InjectedFailure();
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (FailRestoration)
            {
                FailRestoration = false;
                Failures++;
                throw new InjectedFailure();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class KnownRollbackStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is InjectedFailure;
    }
}
