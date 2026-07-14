// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Cross-provider conformance for atomic job enqueue via commit coordination (separate from the distributed-lock
/// coordination suite in <c>JobsCoordinationConformanceTests</c>). Proves the EF coordinated-write seam (U2) and the
/// manager routing (U3/U4) hold identically on every relational backend:
/// <list type="bullet">
/// <item>AE1 — a domain write + <c>AddAsync</c> in one enlisted transaction commit together.</item>
/// <item>AE1 (rollback) — they discard together; the AsyncLocal-capture regression net (a stranded capture would
/// take the direct path and auto-commit the row, leaving it after the coordinated rollback).</item>
/// <item>AE2 — two enqueues in one scope both commit.</item>
/// <item>AE4 — no coordinator → <c>AddAsync</c> inserts directly.</item>
/// <item>R5 — <c>AddBatchAsync</c> commits / rolls back atomically with the caller's transaction.</item>
/// <item>R6 — cron <c>AddAsync</c> commits / rolls back atomically.</item>
/// <item>R13 — <c>IJobScheduler</c> preserves enlisted writes and deferred scheduler wake-ups.</item>
/// <item>Capstone — domain write + outbox publish + job enqueue commit or roll back as one unit.</item>
/// </list>
/// Each leaf derives a sealed class with <c>[Collection&lt;TFixture&gt;]</c> and re-declares the methods with
/// <c>[Fact]</c> so the runner discovers them per provider.
/// <para>
/// AE3's fail-loud modes (dead-transaction throw; non-relational fallback) are intentionally <b>not</b> covered
/// here: the real EF/Postgres/SqlServer enlist always captures a non-null transaction and always exposes
/// <c>IRelationalCommitContext</c>, so neither state can arise through the production coordinator. Reproducing them
/// would require injecting a fake relational context into a real host, which just relocates the unit test with no
/// added fidelity — so they stay unit-only in <c>JobsManagerCoordinatedRoutingTests</c>
/// (<c>TimeJob_dead_transaction_throws_and_persists_nothing</c>,
/// <c>TimeJob_coordinator_without_relational_capability_takes_direct_path</c>,
/// <c>TimeJob_relational_coordinator_but_non_coordinated_provider_throws_mis_wire</c>).
/// </para>
/// </summary>
public abstract class JobsEnqueueAtomicityConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private sealed record CapstoneMessage(Guid JobId);

    public virtual async Task coordinated_save_hooks_isolate_tracked_entries_per_context()
    {
        var ct = AbortToken;
        var observer = new SavePipelineObserver();

        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildCoordinatedEnqueueHost<ObservableJobsDbContext>(
            "node-a",
            options => options.AddInterceptors(new SavePipelineInterceptor(observer))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<ObservableJobsDbContext>(host, ct);
        await host.StartAsync(ct);
        ObservableJobsDbContext.Observer = observer;

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var probe = new SaveHookProbe { Id = Guid.NewGuid() };
            var job = _TimeJob();

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await using var scope = host.Services.CreateAsyncScope();
                    var caller = scope.ServiceProvider.GetRequiredService<ObservableJobsDbContext>();
                    caller.Database.SetDbConnection(connection, contextOwnsConnection: false);
#pragma warning disable MA0045 // Enlisting the existing transaction is an in-memory operation.
                    caller.Database.UseTransaction(transaction);
#pragma warning restore MA0045

                    observer.Reset();
                    observer.RegisterCaller(caller.ContextId.InstanceId);
                    caller.Add(probe);

                    (await manager.AddAsync(job, innerCt)).Should().NotBeNull();
                    await caller.SaveChangesAsync(innerCt);
                },
                ct
            );

            // EF hooks execute once per SaveChangesAsync call. The contract is tracker isolation between the
            // caller and coordinated contexts, not one global hook invocation per ambient transaction.
            var invocations = observer.Invocations;
            invocations.Should().HaveCount(4);
            invocations.Select(x => x.ContextId).Distinct().Should().HaveCount(2);

            var expectedMarkers = new[]
            {
                new SavePipelineMarker(SaveHookKind.Override, SaveContextRole.Caller, nameof(SaveHookProbe), probe.Id),
                new SavePipelineMarker(
                    SaveHookKind.Interceptor,
                    SaveContextRole.Caller,
                    nameof(SaveHookProbe),
                    probe.Id
                ),
                new SavePipelineMarker(
                    SaveHookKind.Override,
                    SaveContextRole.Coordinated,
                    nameof(TimeJobEntity),
                    job.Id
                ),
                new SavePipelineMarker(
                    SaveHookKind.Interceptor,
                    SaveContextRole.Coordinated,
                    nameof(TimeJobEntity),
                    job.Id
                ),
            };

            var markers = invocations
                .SelectMany(invocation =>
                    invocation.Entries.Select(entry => new SavePipelineMarker(
                        invocation.Hook,
                        invocation.Role,
                        entry.EntityType,
                        entry.EntityId
                    ))
                )
                .CountBy(marker => marker)
                .ToDictionary();

            markers.Should().HaveCount(expectedMarkers.Length);
            foreach (var marker in expectedMarkers)
            {
                _AssertInvocation(invocations, marker);
                markers.TryGetValue(marker, out var count).Should().BeTrue();
                count.Should().Be(1);
            }

            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
        }
        finally
        {
            ObservableJobsDbContext.Observer = null;
            await host.StopAsync(ct);
        }
    }

    public virtual async Task domain_message_and_job_commit_atomically()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct, includeMessaging: true);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var publisher = host.Services.GetRequiredService<IOutboxBus>();
            var job = _TimeJob();

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                    await publisher.PublishAsync(new CapstoneMessage(job.Id), cancellationToken: innerCt);
                    await manager.AddAsync(job, innerCt);
                },
                ct
            );

            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            (await fixture.CountPublishedMessagesAsync(host.Services, ct)).Should().Be(1);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task rollback_discards_domain_message_and_job()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct, includeMessaging: true);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var publisher = host.Services.GetRequiredService<IOutboxBus>();
            var job = _TimeJob();
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, innerCt) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                        await publisher.PublishAsync(new CapstoneMessage(job.Id), cancellationToken: innerCt);
                        await manager.AddAsync(job, innerCt);
                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountProbeRowsAsync(ct)).Should().Be(0);
            (await fixture.CountPublishedMessagesAsync(host.Services, ct)).Should().Be(0);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task domain_write_and_enqueue_commit_atomically()
    {
        var ct = AbortToken;
        var sideEffectsProbe = new JobsSideEffectsProbe();
        using var host = await _StartHostAsync(ct, sideEffectsProbe: sideEffectsProbe);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var request = new CoordinatedFacadeRequest(Guid.NewGuid(), "facade commit");
            var options = new Headless.Jobs.Models.EnqueueOptions
            {
                Description = "facade coordinated enqueue",
                Retries = 3,
                RetryIntervals = [2, 5, 8],
                OnNodeDeath = NodeDeathPolicy.MarkFailed,
            };
            var scheduledId = Guid.Empty;

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (connection, transaction, innerCt) =>
                {
                    await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                    (await manager.AddAsync(_TimeJob(), innerCt)).Should().NotBeNull();
                    scheduledId = await scheduler.EnqueueAsync(request, options, innerCt);

                    scheduledId.Should().NotBeEmpty();
                    sideEffectsProbe.RestartCount.Should().Be(0, "scheduler wake-up must wait for commit");
                    sideEffectsProbe.NotificationIds.Should().BeEmpty("notifications must wait for commit");
                },
                ct
            );

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2);
            (await fixture.CountProbeRowsAsync(ct)).Should().Be(1);
            sideEffectsProbe.RestartCount.Should().Be(2);
            sideEffectsProbe.NotificationIds.Should().HaveCount(2).And.Contain(scheduledId);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var persisted = await persistence.GetTimeJobByIdAsync(scheduledId, ct);

            persisted.Should().NotBeNull();
            persisted!.Function.Should().Be(JobsCoordinationFixtureExtensions.CoordinatedFacadeFunctionName);
            persisted.Request.Should().NotBeNull();
            JobsHelper.ReadJobRequest<CoordinatedFacadeRequest>(persisted.Request!).Should().Be(request);
            persisted.Description.Should().Be(options.Description);
            persisted.Retries.Should().Be(options.Retries);
            persisted.RetryIntervals.Should().Equal(options.RetryIntervals!);
            persisted.OnNodeDeath.Should().Be(options.OnNodeDeath);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task rollback_discards_enqueue_and_domain_write()
    {
        var ct = AbortToken;
        var sideEffectsProbe = new JobsSideEffectsProbe();
        using var host = await _StartHostAsync(ct, sideEffectsProbe: sideEffectsProbe);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (connection, transaction, innerCt) =>
                    {
                        await JobsCoordinationFixtureExtensions.InsertProbeRowAsync(connection, transaction, innerCt);
                        await manager.AddAsync(_TimeJob(), innerCt);
                        (
                            await scheduler.EnqueueAsync(
                                new CoordinatedFacadeRequest(Guid.NewGuid(), "facade rollback"),
                                cancellationToken: innerCt
                            )
                        )
                            .Should()
                            .NotBeEmpty();
                        sideEffectsProbe.RestartCount.Should().Be(0, "scheduler wake-up must wait for commit");
                        sideEffectsProbe.NotificationIds.Should().BeEmpty("notifications must wait for commit");

                        // Abandon the scope after the writes are buffered: the transaction never commits.
                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);

            // The regression net: a stranded AsyncLocal capture would have direct-inserted (own connection, committed)
            // and left the row behind. Both writes must be gone.
            (await fixture.CountTimeJobsAsync(ct))
                .Should()
                .Be(0);
            (await fixture.CountProbeRowsAsync(ct)).Should().Be(0);
            sideEffectsProbe.RestartCount.Should().Be(0);
            sideEffectsProbe.NotificationIds.Should().BeEmpty();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // Two independent AddAsync calls co-commit atomically (count == 2). Cross-call ordering is caller-determined, not
    // an R3 guarantee; R3 (insertion order within one write) is asserted at the unit level on the seam array.
    public virtual async Task two_enqueues_in_one_scope_both_commit()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, innerCt) =>
                {
                    (await manager.AddAsync(_TimeJob(), innerCt)).Should().NotBeNull();
                    (await manager.AddAsync(_TimeJob(), innerCt)).Should().NotBeNull();
                },
                ct
            );

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R5: a batched AddBatchAsync writes every row inside the caller's transaction and commits with it.
    public virtual async Task batch_enqueue_commits_atomically()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, innerCt) =>
                {
                    var jobs = new List<TimeJobEntity> { _TimeJob(), _TimeJob() };
                    (await manager.AddBatchAsync(jobs, innerCt)).Should().HaveCount(2);
                },
                ct
            );

            (await fixture.CountTimeJobsAsync(ct)).Should().Be(2);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R5 (rollback): the whole batch discards with the caller's transaction — no partial commit, no stranded rows.
    public virtual async Task batch_enqueue_rolls_back()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, innerCt) =>
                    {
                        var jobs = new List<TimeJobEntity> { _TimeJob(), _TimeJob() };
                        await manager.AddBatchAsync(jobs, innerCt);

                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task enqueue_without_coordinator_inserts_directly()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

            // No ambient coordinated scope: AddAsync takes the direct path and the row is immediately visible.
            var result = await manager.AddAsync(_TimeJob(), ct);

            result.Should().NotBeNull();
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task cron_enqueue_commits_atomically()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var before = await fixture.CountCronJobsAsync(ct);

            await fixture.RunCoordinatedTransactionAsync(
                host.Services,
                async (_, _, innerCt) => (await manager.AddAsync(_CronJob(), innerCt)).Should().NotBeNull(),
                ct
            );

            (await fixture.CountCronJobsAsync(ct)).Should().Be(before + 1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task cron_enqueue_rolls_back()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var before = await fixture.CountCronJobsAsync(ct);
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, innerCt) =>
                    {
                        await manager.AddAsync(_CronJob(), innerCt);

                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountCronJobsAsync(ct)).Should().Be(before);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // R6 (batch rollback): the whole cron batch discards with the caller's transaction — no partial commit, no stranded rows.
    public virtual async Task cron_batch_enqueue_rolls_back()
    {
        var ct = AbortToken;
        using var host = await _StartHostAsync(ct);

        try
        {
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var before = await fixture.CountCronJobsAsync(ct);
            var sentinel = new InvalidOperationException("force rollback");

            var act = () =>
                fixture.RunCoordinatedTransactionAsync(
                    host.Services,
                    async (_, _, innerCt) =>
                    {
                        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };
                        await manager.AddBatchAsync(crons, innerCt);

                        throw sentinel;
                    },
                    ct
                );

            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(sentinel);
            (await fixture.CountCronJobsAsync(ct)).Should().Be(before);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task<IHost> _StartHostAsync(
        CancellationToken cancellationToken,
        bool includeMessaging = false,
        JobsSideEffectsProbe? sideEffectsProbe = null
    )
    {
        await fixture.ResetDatabaseAsync(cancellationToken);
        var host = fixture.BuildCoordinatedEnqueueHost("node-a", includeMessaging, sideEffectsProbe);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, cancellationToken);
        await fixture.CreateProbeTableAsync(cancellationToken);

        if (includeMessaging)
        {
            await host.Services.GetRequiredService<IStorageInitializer>().InitializeAsync(cancellationToken);
        }

        await host.StartAsync(cancellationToken);

        return host;
    }

    private static TimeJobEntity _TimeJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Request = [],
            // Far-future so the deferred immediate-dispatch branch never runs — keeps the assertion on row presence.
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };

    private static CronJobEntity _CronJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            Description = JobsCoordinationFixtureExtensions.CoordinatedFunctionName,
            // CronScheduleCache parses with IncludingSeconds = true, so the expression has six fields.
            Expression = "0 0 0 * * *",
            Request = [],
        };

    private static void _AssertInvocation(
        IReadOnlyCollection<SavePipelineInvocation> invocations,
        SavePipelineMarker marker
    )
    {
        var invocation = invocations.Should().ContainSingle(x => x.Hook == marker.Hook && x.Role == marker.Role).Which;
        invocation.Ordinal.Should().Be(1);
        invocation
            .Entries.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new TrackedEntry(marker.EntityType, marker.EntityId));
    }

    private sealed class ObservableJobsDbContext(DbContextOptions<ObservableJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options)
    {
        public static SavePipelineObserver? Observer { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Observer?.Record(SaveHookKind.Override, this);
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SaveHookProbe>(builder =>
            {
                builder.ToTable("jobs_probe");
                builder.HasKey(x => x.Id);
            });
        }
    }

    private sealed class SavePipelineInterceptor(SavePipelineObserver observer) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (eventData.Context is not null)
            {
                observer.Record(SaveHookKind.Interceptor, eventData.Context);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class SavePipelineObserver
    {
        private readonly Lock _lock = new();
        private readonly List<SavePipelineInvocation> _invocations = [];
        private Guid? _callerContextId;

        public IReadOnlyCollection<SavePipelineInvocation> Invocations
        {
            get
            {
                lock (_lock)
                {
                    return [.. _invocations];
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _callerContextId = null;
                _invocations.Clear();
            }
        }

        public void RegisterCaller(Guid contextId)
        {
            lock (_lock)
            {
                _callerContextId = contextId;
            }
        }

        public void Record(SaveHookKind hook, DbContext context)
        {
            lock (_lock)
            {
                if (_callerContextId is null)
                {
                    return;
                }
            }

            var contextId = context.ContextId.InstanceId;
            var entries = context
                .ChangeTracker.Entries()
                .Where(x => x.State is EntityState.Added or EntityState.Modified)
                .Select(x => new TrackedEntry(x.Metadata.ClrType.Name, (Guid)x.Property("Id").CurrentValue!))
                .OrderBy(x => x.EntityType, StringComparer.Ordinal)
                .ThenBy(x => x.EntityId)
                .ToArray();

            lock (_lock)
            {
                var callerContextId = _callerContextId;
                if (callerContextId is null)
                {
                    return;
                }

                var role = contextId == callerContextId ? SaveContextRole.Caller : SaveContextRole.Coordinated;
                var ordinal = _invocations.Count(x => x.Hook == hook && x.ContextId == contextId) + 1;
                _invocations.Add(new SavePipelineInvocation(hook, role, contextId, ordinal, entries));
            }
        }
    }

    private sealed class SaveHookProbe
    {
        public Guid Id { get; set; }
    }

    private sealed record TrackedEntry(string EntityType, Guid EntityId);

    private sealed record SavePipelineInvocation(
        SaveHookKind Hook,
        SaveContextRole Role,
        Guid ContextId,
        int Ordinal,
        IReadOnlyCollection<TrackedEntry> Entries
    );

    private sealed record SavePipelineMarker(SaveHookKind Hook, SaveContextRole Role, string EntityType, Guid EntityId);

    private enum SaveHookKind
    {
        Override,
        Interceptor,
    }

    private enum SaveContextRole
    {
        Caller,
        Coordinated,
    }
}
