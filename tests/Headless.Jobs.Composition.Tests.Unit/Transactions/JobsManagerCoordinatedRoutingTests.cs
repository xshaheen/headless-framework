// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transactions;

/// <summary>
/// Unit coverage for <see cref="JobsManager{TTimeJob,TCronJob}" /> commit-coordination routing: the synchronous
/// capture fork, the fail-loud cases, and the post-commit signal hand-off to the hosted worker. Atomicity itself
/// (rows committing / discarding with the caller's transaction) is integration-only — see the EF harness conformance
/// suite; the worker's own bounds are covered by <see cref="JobsPostCommitSignalServiceTests" />.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed partial class JobsManagerCoordinatedRoutingTests : TestBase
{
    private const string _FunctionName = "routing-test-fn";

    // Every wait on worker progress is bounded so a regression fails the test instead of hanging the run.
    private static readonly TimeSpan _WaitTimeout = TimeSpan.FromSeconds(30);
    private readonly List<JobsPostCommitSignalService> _workers = [];
    private readonly List<UnitOfWorkProbe> _scopes = [];

    public JobsManagerCoordinatedRoutingTests()
    {
        _BuildProvider();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var worker in _workers)
        {
            await worker.StopAsync(AbortToken);
            worker.Dispose();
        }

        // Newest first: a scope opened inside another must be disposed before it.
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            await _scopes[i].DisposeAsync();
        }

        JobFunctionProvider.ResetForTests();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task time_job_without_coordinator_takes_direct_path()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);

        var result = await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        result.Should().NotBeNull();
        await sut.Persistence.Received(1).AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddTimeJobNotifyAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task required_atomic_time_job_rejects_missing_capability_before_schedule_effects()
    {
        var middlewareCalls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, cancellationToken) =>
            {
                middlewareCalls++;
                return next(cancellationToken);
            }
        );
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var candidate = _FutureTimeJob();
        candidate.Enlistment = TransactionEnlistment.Required;

        var schedule = () => sut.Time.AddAsync(candidate, AbortToken);

        await schedule
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires an active unit of work*");
        middlewareCalls.Should().Be(0);
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
        await sut.Notification.DidNotReceiveWithAnyArgs().AddTimeJobNotifyAsync(default);
    }

    [Fact]
    public async Task immediate_dispatch_threads_the_persisted_tenant_into_the_dispatched_state()
    {
        // #278: the immediate-dispatch branch builds JobExecutionState from the ACQUIRED row via
        // _BuildContextFromNonGeneric (JobsManager.cs:532). The execute middleware restores the tenant from that state,
        // so a copy-paste slip on the TenantId assignment would silently dispatch the job system-scope. Pin it here.
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false, dispatcherEnabled: true);
        var acquiredChild = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _FunctionName,
            TenantId = "t-child",
        };
        var acquired = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _FunctionName,
            TenantId = "t-root",
            ExecutionTime = DateTime.UtcNow,
            Children = [acquiredChild],
        };
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns([acquired]);
        JobExecutionState[]? dispatched = null;
        sut.Dispatcher.DispatchAsync(
                Arg.Do<JobExecutionState[]>(states => dispatched = states),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        await sut.Time.AddAsync(_ImmediateTimeJob(), AbortToken);

        var rootState = dispatched.Should().ContainSingle().Which;
        rootState.TenantId.Should().Be("t-root");
        rootState.TimeJobChildren.Should().ContainSingle().Which.TenantId.Should().Be("t-child");
    }

    [Fact]
    public async Task add_stamps_the_entire_chain_with_injected_identity_and_time_services()
    {
        var now = new DateTimeOffset(2026, 7, 18, 9, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var rootId = Guid.Parse("01981f40-29c0-7000-8000-000000000001");
        var childId = Guid.Parse("01981f40-29c0-7000-8000-000000000002");
        var grandChildId = Guid.Parse("01981f40-29c0-7000-8000-000000000003");
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(rootId, childId, grandChildId);
        var sut = _CreateSut(
            CoordinatorMode.None,
            withWriter: false,
            timeProvider: timeProvider,
            guidGenerator: guidGenerator
        );
        // A pre-built root -> child -> grandchild tree with no ids or parent links, but every node carries distinct
        // stale (non-now) CreatedAt/UpdatedAt. The manager must OVERWRITE those with its injected clock — asserted
        // below — not merely fill defaults on unstamped nodes.
        var stale = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var job = new TimeJobEntity
        {
            Function = _FunctionName,
            ExecutionTime = now.UtcDateTime.AddHours(1),
            CreatedAt = stale,
            UpdatedAt = stale.AddDays(1),
            Children =
            [
                new TimeJobEntity
                {
                    Function = _FunctionName,
                    RunCondition = RunCondition.OnSuccess,
                    CreatedAt = stale.AddDays(2),
                    UpdatedAt = stale.AddDays(3),
                    Children =
                    [
                        new TimeJobEntity
                        {
                            Function = _FunctionName,
                            RunCondition = RunCondition.OnSuccess,
                            CreatedAt = stale.AddDays(4),
                            UpdatedAt = stale.AddDays(5),
                        },
                    ],
                },
            ],
        };

        var result = await sut.Time.AddAsync(job, AbortToken);

        var child = result.Children.Should().ContainSingle().Subject;
        var grandChild = child.Children.Should().ContainSingle().Subject;
        result.Id.Should().Be(rootId);
        result.ParentId.Should().BeNull();
        child.Id.Should().Be(childId);
        child.ParentId.Should().Be(rootId);
        grandChild.Id.Should().Be(grandChildId);
        grandChild.ParentId.Should().Be(childId);
        foreach (var item in new[] { result, child, grandChild })
        {
            item.CreatedAt.Should().Be(now);
            item.UpdatedAt.Should().Be(now);
        }
    }

    [Fact]
    public async Task schedule_middleware_that_omits_next_rejects_before_direct_or_coordinated_write()
    {
        using (_ReplaceScheduleDispatch((_, _, _) => Task.CompletedTask))
        {
            var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);

            var act = () => sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

            await act.Should().ThrowAsync<JobValidatorException>();
            await sut
                .Persistence.DidNotReceive()
                .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
            await sut
                .Writer.DidNotReceive()
                .WriteTimeJobsAsync(
                    Arg.Any<TimeJobEntity[]>(),
                    Arg.Any<IRelationalUnitOfWorkResource>(),
                    Arg.Any<CancellationToken>()
                );
            sut.Coordinator!.OnCommitCount.Should().Be(0);
            sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
        }
    }

    [Fact]
    public async Task Schedule_middleware_that_omits_next_aborts_the_entire_batch_before_one_writer_call()
    {
        using (_ReplaceScheduleDispatch((_, _, _) => Task.CompletedTask))
        {
            var sut = _CreateSut(CoordinatorMode.None, withWriter: false);

            var act = () => sut.Time.AddBatchAsync([_FutureTimeJob(), _FutureTimeJob()], AbortToken);

            await act.Should().ThrowAsync<JobValidatorException>();
            await sut
                .Persistence.DidNotReceive()
                .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
            await sut.Notification.DidNotReceive().AddTimeJobsBatchNotifyAsync();
        }
    }

    [Fact]
    public async Task Time_batch_schedule_middleware_runs_before_manager_normalizes_the_entity()
    {
        DateTime? executionTimeSeenByMiddleware = DateTime.MaxValue;
        using (
            _ReplaceScheduleDispatch(
                (context, next, token) =>
                {
                    executionTimeSeenByMiddleware = ((TimeJobEntity)context.Job).ExecutionTime;
                    return next(token);
                }
            )
        )
        {
            var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
            var job = _FutureTimeJob();
            job.ExecutionTime = null;

            await sut.Time.AddBatchAsync([job], AbortToken);

            executionTimeSeenByMiddleware.Should().BeNull();
            job.ExecutionTime.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Cron_batch_schedule_middleware_runs_before_expression_validation()
    {
        using (
            _ReplaceScheduleDispatch(
                (context, next, token) =>
                {
                    ((CronJobEntity)context.Job).Expression = "0 0 0 * * *";
                    return next(token);
                }
            )
        )
        {
            var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
            var job = _CronJob();
            job.Expression = "invalid";

            var result = await sut.Cron.AddBatchAsync([job], AbortToken);

            result.Should().ContainSingle().Which.Should().BeSameAs(job);
        }
    }

    [Fact]
    public async Task time_job_coordinator_without_relational_capability_takes_direct_path()
    {
        // A messaging-only coordinated scope: coordination must not become infectious — fall back to direct insert.
        var sut = _CreateSut(CoordinatorMode.NonRelational, withWriter: true);

        var result = await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        result.Should().NotBeNull();
        await sut.Persistence.Received(1).AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Coordinator!.OnCommitCount.Should().Be(0);
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task time_job_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);

        var act = () => sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task time_job_unit_of_work_completed_during_schedule_pipeline_throws_and_persists_nothing()
    {
        // KTD7: the drift re-validation. The capture happens synchronously before the schedule middleware runs; if
        // the SAME unit of work completes underneath the in-flight schedule (racing completion elsewhere in the
        // scope), the write must refuse to enlist in a unit that already reached its outcome rather than silently
        // falling back to the direct path.
        Sut? sut = null;
        using var dispatch = _ReplaceScheduleDispatch(
            async (_, next, ct) =>
            {
                await sut!.Coordinator!.CommitAsync();
                await next(ct);
            }
        );
        sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);

        var act = () => sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer active*");
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task time_job_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        // Live relational coordinator, but the provider cannot write inside the ambient transaction (in-memory shape).
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);

        var act = () => sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task cron_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);

        var act = () => sut.Cron.AddAsync(_CronJob(), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
        await sut
            .Writer.DidNotReceive()
            .WriteCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task cron_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);

        var act = () => sut.Cron.AddAsync(_CronJob(), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_report_cron_update_success_when_post_commit_notification_fails()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var current = _CronJob();
        current.ScheduleRevision = 4;
        var update = _CronJob();
        update.Id = current.Id;
        update.Expression = current.Expression;
        sut.Persistence.GetCronJobByIdAsync(current.Id, AbortToken).Returns(current);
        sut.Persistence.UpdateCronJobsAtomicallyAsync(
                Arg.Any<CronJobAtomicUpdate<CronJobEntity>[]>(),
                Arg.Any<DateTimeOffset>(),
                AbortToken
            )
            .Returns([update]);
        var failure = new InvalidOperationException("notification offline");
        sut.Notification.UpdateCronJobNotifyAsync(update).Returns(_ => throw failure);

        var result = await sut.Cron.UpdateAsync(update, AbortToken);

        result.IsSucceeded.Should().BeTrue();
        result.Result.Should().BeSameAs(update);
        result.AffectedRows.Should().Be(1);
        sut.Logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Warning && x.Exception == failure);
    }

    [Fact]
    public async Task time_job_batch_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };

        var act = () => sut.Time.AddBatchAsync(jobs, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task time_job_batch_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };

        var act = () => sut.Time.AddBatchAsync(jobs, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task cron_batch_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };

        var act = () => sut.Cron.AddBatchAsync(crons, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
        await sut
            .Writer.DidNotReceive()
            .WriteCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task cron_batch_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };

        var act = () => sut.Cron.AddBatchAsync(crons, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task time_job_live_coordinator_writes_in_transaction_and_signals_side_effects_on_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var job = _FutureTimeJob();
        var notified = _NotifiedTimeJob(sut);

        var result = await sut.Time.AddAsync(job, AbortToken);

        result.Should().BeSameAs(job);
        await sut
            .Writer.Received(1)
            .WriteTimeJobsAsync(
                Arg.Is<TimeJobEntity[]>(a => a.Length == 1 && a[0].Id == job.Id),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Side effects must NOT have fired synchronously and the row must NOT have gone through the direct insert.
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddTimeJobNotifyAsync(Arg.Any<Guid>());

        await sut.Coordinator.CommitAsync();

        // The commit callback only hands the worker a signal: it completes synchronously and runs nothing itself.
        sut.Coordinator.SynchronousCommitCallbacks.Should().Be(1);
        sut.Signals.PendingCount.Should().Be(1);
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddTimeJobNotifyAsync(Arg.Any<Guid>());

        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        sut.Scheduler.Received(1).RestartIfNeeded(job.ExecutionTime!.Value);
        await sut.Notification.Received(1).AddTimeJobNotifyAsync(job.Id);
    }

    [Fact]
    public async Task post_commit_side_effect_failure_is_logged_by_the_worker_and_never_reaches_the_commit()
    {
        // KTD-4 crash isolation: once the row is durably committed, a post-commit side-effect failure must NOT
        // surface as a caller error after a successful commit — the worker logs it against the job scope and the
        // polling sweep recovers.
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var boom = new InvalidOperationException("post-commit side effect boom");
        sut.Notification.AddTimeJobNotifyAsync(Arg.Any<Guid>()).Returns(Task.FromException(boom));

        await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        var drain = () => sut.Coordinator!.CommitAsync();
        await drain.Should().NotThrowAsync();
        await _StartWorkerAsync(sut);

        var entry = await sut.SignalsLogger.WaitForAsync(e => e.Exception is not null, AbortToken);
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeSameAs(boom);
        sut.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task worker_dispatches_an_immediately_due_job_after_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: true);
        var job = _ImmediateTimeJob();
        // AddAsync re-stamps the entity id, so the acquired row is built from the id the manager actually asks for.
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                [
                    new TimeJobEntity
                    {
                        Id = call.Arg<Guid[]>().Single(),
                        Function = _FunctionName,
                        ExecutionTime = job.ExecutionTime,
                    },
                ]
            );
        var notified = _NotifiedTimeJob(sut);

        await sut.Time.AddAsync(job, AbortToken);
        await sut.Coordinator!.CommitAsync();
        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        await sut
            .Dispatcher.Received(1)
            .DispatchAsync(
                Arg.Is<JobExecutionState[]>(states => states.Single().JobId == job.Id),
                Arg.Any<CancellationToken>()
            );
        sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
    }

    [Fact]
    public async Task worker_arms_the_scheduler_restart_for_a_job_due_later()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: true);
        var job = _FutureTimeJob();
        var notified = _NotifiedTimeJob(sut);

        await sut.Time.AddAsync(job, AbortToken);
        await sut.Coordinator!.CommitAsync();
        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        sut.Scheduler.Received(1).RestartIfNeeded(job.ExecutionTime!.Value);
        await sut
            .Persistence.DidNotReceive()
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task worker_re_reads_the_clock_so_a_job_that_became_due_by_commit_time_is_dispatched()
    {
        // Enqueued five seconds before commit with a due time equal to the commit instant: at enqueue it is a
        // "later" job; by the time the worker runs it is due, so it must be acquired rather than scheduled.
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        var sut = _CreateSut(
            CoordinatorMode.LiveRelational,
            withWriter: true,
            dispatcherEnabled: true,
            timeProvider: timeProvider
        );
        var job = _FutureTimeJob();
        job.ExecutionTime = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(5);
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns([]);
        var notified = _NotifiedTimeJob(sut);

        await sut.Time.AddAsync(job, AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await sut.Coordinator!.CommitAsync();
        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        await sut
            .Persistence.Received(1)
            .AcquireImmediateTimeJobsAsync(Arg.Is<Guid[]>(ids => ids.Single() == job.Id), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
    }

    [Fact]
    public async Task full_signal_channel_drops_the_time_job_signal_and_the_commit_still_completes()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        _FillSignalChannel(sut);

        await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);
        var drain = () => sut.Coordinator!.CommitAsync();

        await drain.Should().NotThrowAsync();
        sut.Coordinator!.SynchronousCommitCallbacks.Should().Be(1);
        sut.Signals.PendingCount.Should().Be(JobsPostCommitSignalService.Capacity);
        sut.SignalsLogger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
    }

    [Fact]
    public async Task cron_cache_invalidation_runs_on_the_commit_path_even_when_the_signal_channel_is_full()
    {
        // R10: the poll sweep reads through the cron-expressions cache, so a dropped invalidation would not be
        // recovered by the sweep — it stays inline on the commit callback and never enters the drop-on-full channel.
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        _FillSignalChannel(sut);

        await sut.Cron.AddAsync(_CronJob(), AbortToken);
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut.Coordinator!.CommitAsync();

        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Signals.PendingCount.Should().Be(JobsPostCommitSignalService.Capacity);
        sut.SignalsLogger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
    }

    [Fact]
    public async Task stalled_cron_cache_invalidation_is_abandoned_after_the_bound_and_the_commit_completes()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, timeProvider: timeProvider);
        var invalidationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Writer.InvalidateCronExpressionsCacheAsync()
            .Returns(_ =>
            {
                invalidationStarted.TrySetResult();

                return neverCompletes.Task;
            });

        await sut.Cron.AddAsync(_CronJob(), AbortToken);
        var drain = sut.Coordinator!.CommitAsync();
        await invalidationStarted.Task.WaitAsync(_WaitTimeout, AbortToken);
        await FakeClock.AdvanceUntilAsync(
            timeProvider,
            JobsPostCommitSignalService.SignalDeadline + TimeSpan.FromTicks(1),
            drain,
            AbortToken
        );

        var drainAction = async () => await drain;
        await drainAction.Should().NotThrowAsync();
        sut.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
        // The restart + notify signal is still handed to the worker: only the cache call was abandoned.
        sut.Signals.PendingCount.Should().Be(1);
        neverCompletes.SetResult();
    }

    [Fact]
    public async Task cron_cache_invalidation_failure_is_logged_and_the_commit_completes()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var boom = new InvalidOperationException("cache offline");
        sut.Writer.InvalidateCronExpressionsCacheAsync().Returns(Task.FromException(boom));

        await sut.Cron.AddAsync(_CronJob(), AbortToken);
        var drain = () => sut.Coordinator!.CommitAsync();

        await drain.Should().NotThrowAsync();
        sut.Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && ReferenceEquals(e.Exception, boom));
        sut.Signals.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task cron_enqueue_in_a_rolled_back_scope_leaves_no_signal_and_no_cache_invalidation()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var cron = _CronJob();

        await sut.Cron.AddAsync(cron, AbortToken);
        await sut.Coordinator!.RollbackAsync();

        // The row went into the caller's transaction (and is discarded with it); nothing else happened.
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Single().Id == cron.Id),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        sut.Signals.PendingCount.Should().Be(0);
        sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
        await sut.Notification.DidNotReceive().AddCronJobNotifyAsync(Arg.Any<CronJobEntity>());
    }

    [Fact]
    public async Task keyed_replace_and_cancel_each_enqueue_one_schedule_changed_signal()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var runId = Guid.NewGuid();
        sut.Writer.WriteKeyedTimeJobAsync(
                Arg.Any<JobKey>(),
                Arg.Any<TimeJobEntity>(),
                Arg.Any<long?>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new JobScheduleResult(JobScheduleDisposition.Replaced, runId, 2, JobStatus.Idle));
        sut.Writer.CancelKeyedTimeJobAsync(
                Arg.Any<JobKeyScope>(),
                Arg.Any<JobKey>(),
                Arg.Any<long>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new JobScheduleResult(JobScheduleDisposition.Cancelled, runId, 2, JobStatus.Cancelled));
        var restarts = 0;
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Scheduler.When(x => x.Restart())
            .Do(_ =>
            {
                if (Interlocked.Increment(ref restarts) == 2)
                {
                    restarted.TrySetResult();
                }
            });

        var replaced = await sut.Time.ScheduleKeyedAsync(new JobKey("order-1"), _FutureTimeJob(), 1, AbortToken);
        var cancelled = await sut.Time.CancelKeyedAsync(
            new JobKeyScope(_FunctionName),
            new JobKey("order-1"),
            2,
            AbortToken
        );

        replaced.IsProvisional.Should().BeTrue();
        cancelled.IsProvisional.Should().BeTrue();
        sut.Coordinator!.OnCommitCount.Should().Be(2);
        sut.Scheduler.DidNotReceive().Restart();

        await sut.Coordinator.CommitAsync();

        sut.Coordinator.SynchronousCommitCallbacks.Should().Be(2);
        sut.Signals.PendingCount.Should().Be(2);

        await _StartWorkerAsync(sut);
        await restarted.Task.WaitAsync(_WaitTimeout, AbortToken);

        sut.Scheduler.Received(2).Restart();
    }

    [Fact]
    public async Task time_job_batch_produces_one_signal_carrying_all_ids()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: true);
        var due = _ImmediateTimeJob();
        var later = _FutureTimeJob();
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns([]);
        // The batch path notifies FIRST and arms the restart LAST, so the restart is the completion hook here.
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Scheduler.When(x => x.RestartIfNeeded(Arg.Any<DateTime?>())).Do(_ => restarted.TrySetResult());

        await sut.Time.AddBatchAsync([due, later], AbortToken);
        await sut.Coordinator!.CommitAsync();

        sut.Coordinator.OnCommitCount.Should().Be(1);
        sut.Signals.PendingCount.Should().Be(1);

        await _StartWorkerAsync(sut);
        await restarted.Task.WaitAsync(_WaitTimeout, AbortToken);

        await sut.Notification.Received(1).AddTimeJobsBatchNotifyAsync();

        await sut
            .Persistence.Received(1)
            .AcquireImmediateTimeJobsAsync(Arg.Is<Guid[]>(ids => ids.Single() == due.Id), Arg.Any<CancellationToken>());
        sut.Scheduler.Received(1).RestartIfNeeded(later.ExecutionTime!.Value);
    }

    [Fact]
    public async Task coordinated_enqueue_with_background_services_disabled_commits_and_the_worker_stays_quiet()
    {
        // DisableBackgroundServices(): the dispatcher and scheduler are no-ops, but the callback shape is uniform —
        // the worker still runs the signal (notify only) and nothing surfaces to the caller.
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: false);
        var job = _ImmediateTimeJob();
        var notified = _NotifiedTimeJob(sut);

        await sut.Time.AddAsync(job, AbortToken);
        var drain = () => sut.Coordinator!.CommitAsync();
        await drain.Should().NotThrowAsync();
        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        await sut
            .Persistence.DidNotReceive()
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
        await sut.Dispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
        sut.SignalsLogger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void jobs_host_registers_the_post_commit_worker_even_with_background_services_disabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITimeJobManager<TimeJobEntity>>().Should().NotBeNull();
        provider
            .GetServices<IHostedService>()
            .Should()
            .ContainSingle(x => x is JobsPostCommitSignalService)
            .Which.Should()
            .BeSameAs(provider.GetRequiredService<JobsPostCommitSignalService>());
    }

    [Fact]
    public async Task coordinated_enqueue_runs_nothing_when_the_scope_rolls_back()
    {
        // Coordinated side effects must fire only on commit — a rollback discards the row, so nothing may reach the
        // worker or the scheduler for work that was rolled back.
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);

        await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        await sut.Coordinator.RollbackAsync();

        sut.Signals.PendingCount.Should().Be(0);
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddTimeJobNotifyAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task time_job_live_coordinator_defers_immediate_dispatch_to_the_worker()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: true);
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns([]);
        var notified = _NotifiedTimeJob(sut);

        await sut.Time.AddAsync(_ImmediateTimeJob(), AbortToken);

        // The immediate-acquire probe is part of the post-commit side effects, not the synchronous enqueue — and not
        // the commit callback either.
        await sut
            .Persistence.DidNotReceive()
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
        await sut.Coordinator!.CommitAsync();
        await sut
            .Persistence.DidNotReceive()
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());

        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        await sut
            .Persistence.Received(1)
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task time_job_batch_live_coordinator_routes_all_and_signals_side_effects_once()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Notification.AddTimeJobsBatchNotifyAsync()
            .Returns(_ =>
            {
                notified.TrySetResult();

                return Task.CompletedTask;
            });

        var result = await sut.Time.AddBatchAsync(jobs, AbortToken);

        result.Should().HaveCount(2);
        // R3: the batch reaches the seam as one array in insertion order (AddRange preserves it downstream).
        await sut
            .Writer.Received(1)
            .WriteTimeJobsAsync(
                Arg.Is<TimeJobEntity[]>(a => a.Length == 2 && a[0].Id == jobs[0].Id && a[1].Id == jobs[1].Id),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);
        await sut.Notification.DidNotReceive().AddTimeJobsBatchNotifyAsync();

        await sut.Coordinator.CommitAsync();
        sut.Signals.PendingCount.Should().Be(1);
        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        await sut.Notification.Received(1).AddTimeJobsBatchNotifyAsync();
    }

    [Fact]
    public async Task time_job_chain_with_negative_descendant_retries_is_rejected_before_persistence()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var job = _FutureTimeJob();
        job.Children =
        [
            new TimeJobEntity
            {
                Function = _FunctionName,
                ExecutionTime = DateTime.UtcNow.AddHours(2),
                Retries = -1,
            },
        ];

        var act = () => sut.Time.AddAsync(job, AbortToken);

        (await act.Should().ThrowAsync<JobValidatorException>()).WithMessage("*Retries must be >= 0*");
        await sut.Persistence.DidNotReceive().AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), AbortToken);
    }

    [Fact]
    public async Task time_job_batch_with_negative_descendant_retries_is_rejected_atomically()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var validJob = _FutureTimeJob();
        var invalidJob = _FutureTimeJob();
        invalidJob.Children =
        [
            new TimeJobEntity
            {
                Function = _FunctionName,
                ExecutionTime = DateTime.UtcNow.AddHours(2),
                Retries = -1,
            },
        ];

        var act = () => sut.Time.AddBatchAsync([validJob, invalidJob], AbortToken);

        (await act.Should().ThrowAsync<JobValidatorException>()).WithMessage("*Retries must be >= 0*");
        await sut.Persistence.DidNotReceive().AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), AbortToken);
        await sut.Notification.DidNotReceive().AddTimeJobsBatchNotifyAsync();
    }

    [Fact]
    public async Task cron_without_coordinator_takes_direct_path()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);

        var result = await sut.Cron.AddAsync(_CronJob(), AbortToken);

        result.Should().NotBeNull();
        await sut
            .Persistence.Received(1)
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task cron_live_coordinator_writes_in_transaction_and_invalidates_the_cache_on_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var cron = _CronJob();
        var notified = _NotifiedCronJob(sut, cron);

        var result = await sut.Cron.AddAsync(cron, AbortToken);

        result.Should().BeSameAs(cron);
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Length == 1 && a[0].Id == cron.Id),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Cache invalidation + scheduler + notify must be deferred — never on a pre-commit snapshot.
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());

        await sut.Coordinator.CommitAsync();

        // The cache invalidation is the one side effect that runs on the commit path (R10); restart + notify are
        // handed to the worker.
        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Signals.PendingCount.Should().Be(1);
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());

        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddCronJobNotifyAsync(cron);
    }

    [Fact]
    public async Task cron_batch_live_coordinator_routes_all_and_signals_side_effects_once()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };
        var notified = _NotifiedCronJob(sut, crons[1]);

        var result = await sut.Cron.AddBatchAsync(crons, AbortToken);

        result.Should().HaveCount(2);
        // R3: the batch reaches the seam as one array in insertion order (AddRange preserves it downstream).
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Length == 2 && a[0].Id == crons[0].Id && a[1].Id == crons[1].Id),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Cache invalidation + scheduler + per-entity notify must be deferred to commit, never on a pre-commit snapshot.
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddCronJobNotifyAsync(Arg.Any<CronJobEntity>());

        await sut.Coordinator.CommitAsync();

        // Cache invalidation fires exactly once for the batch on the commit path; one signal carries the rest.
        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Signals.PendingCount.Should().Be(1);

        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        // Scheduler restarts once; notify fires per entity.
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        foreach (var cron in crons)
        {
            await sut.Notification.Received(1).AddCronJobNotifyAsync(cron);
        }
    }

    [Fact]
    public async Task cron_batch_with_negative_retries_is_rejected_atomically()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var validCron = _CronJob();
        var invalidCron = _CronJob();
        invalidCron.Retries = -1;

        var act = () => sut.Cron.AddBatchAsync([validCron, invalidCron], AbortToken);

        (await act.Should().ThrowAsync<JobValidatorException>()).WithMessage("*Retries must be >= 0*");
        await sut.Persistence.DidNotReceive().InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), AbortToken);
    }

    [Fact]
    public async Task cron_add_rejects_an_undefined_missed_run_policy_before_persistence()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var cron = _CronJob();
        cron.OnMissedRun = (MissedRunPolicy)999;

        var act = () => sut.Cron.AddAsync(cron, AbortToken);

        (await act.Should().ThrowAsync<JobValidatorException>()).WithMessage("*not defined*");
        await sut.Persistence.DidNotReceive().InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), AbortToken);
    }

    [Fact]
    public async Task cron_update_rejects_non_positive_missed_run_grace_before_reading_storage()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var cron = _CronJob();
        cron.MissedRunGraceSeconds = 0;

        var result = await sut.Cron.UpdateAsync(cron, AbortToken);

        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeOfType<JobValidatorException>().Which.Message.Should().Contain("greater than zero");
        await sut.Persistence.DidNotReceive().GetCronJobByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task cron_add_batch_aggregates_invalid_recovery_settings_and_writes_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var invalidPolicy = _CronJob();
        invalidPolicy.OnMissedRun = (MissedRunPolicy)999;
        var invalidGrace = _CronJob();
        invalidGrace.MissedRunGraceSeconds = -1;

        var act = () => sut.Cron.AddBatchAsync([invalidPolicy, invalidGrace], AbortToken);

        var exception = (await act.Should().ThrowAsync<JobValidatorException>()).Which;
        exception.Message.Should().Contain("not defined").And.Contain("greater than zero");
        await sut.Persistence.DidNotReceive().InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), AbortToken);
    }

    [Fact]
    public async Task cron_update_batch_rejects_invalid_recovery_settings_before_reading_storage()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);
        var cron = _CronJob();
        cron.OnMissedRun = (MissedRunPolicy)999;

        var result = await sut.Cron.UpdateBatchAsync([cron], AbortToken);

        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeOfType<JobValidatorException>().Which.Message.Should().Contain("not defined");
        await sut
            .Persistence.DidNotReceive()
            .GetCronJobsAsync(Arg.Any<Expression<Func<CronJobEntity, bool>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void jobs_only_host_resolves_scoped_manager_with_no_active_unit_of_work()
    {
        // KD5: the facade is scoped, resolved from a scope, with the scope's IUnitOfWorkManager reporting no active
        // unit — the direct-path condition — when the host never begins one.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ITimeJobManager<TimeJobEntity>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Current.Should().BeNull();
    }

    private static TimeJobEntity _FutureTimeJob()
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Function = _FunctionName,
            Description = _FunctionName,
            Request = [],
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };
    }

    private static TimeJobEntity _ImmediateTimeJob()
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Function = _FunctionName,
            Description = _FunctionName,
            Request = [],
            ExecutionTime = DateTime.UtcNow,
        };
    }

    [Fact]
    public async Task cron_direct_add_persists_the_store_anchored_position_and_arms_the_restart_from_it()
    {
        // #817 + R4b. The definition is positioned from the anchor the STORE reported inside the inserting
        // transaction, and the scheduler wake is armed from what came back — not from a projection this node computed
        // off its own clock before persisting. Under skew those two disagree, and the row is the one that matters.
        var nodeClock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false, timeProvider: nodeClock);
        var cron = _CronJob();

        await sut.Cron.AddAsync(cron, AbortToken);

        // Expression is "0 0 0 * * *" (midnight daily), so the first occurrence after the 2031-03-04T05:06:07 anchor
        // is 2031-03-05T00:00:00 — a value unreachable from the node clock.
        cron.ReconciledThroughUtc.Should().Be(_StoreAnchorUtc);
        cron.NextDueUtc.Should().Be(new DateTime(2031, 3, 5, 0, 0, 0, DateTimeKind.Utc));
        cron.EvaluationFingerprint.Should().NotBeNullOrEmpty();
        sut.Scheduler.Received(1).RestartIfNeeded(new DateTime(2031, 3, 5, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task cron_batch_direct_add_arms_the_restart_from_the_earliest_persisted_position()
    {
        var nodeClock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false, timeProvider: nodeClock);
        var hourly = _CronJob();
        hourly.Expression = "0 0 * * * *";
        var daily = _CronJob();
        var crons = new List<CronJobEntity> { daily, hourly };

        await sut.Cron.AddBatchAsync(crons, AbortToken);

        hourly.ReconciledThroughUtc.Should().Be(_StoreAnchorUtc);
        daily.ReconciledThroughUtc.Should().Be(_StoreAnchorUtc);
        // The hourly definition fires first, so it — not the batch's first element — decides the wake.
        sut.Scheduler.Received(1).RestartIfNeeded(new DateTime(2031, 3, 4, 6, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task coordinated_cron_add_defers_a_restart_armed_from_the_persisted_position()
    {
        // The coordinated closure used to capture the pre-persistence projection. It must capture what the write
        // returned, or the deferred wake and the committed row describe different schedules.
        var nodeClock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, timeProvider: nodeClock);
        var cron = _CronJob();
        var notified = _NotifiedCronJob(sut, cron);

        await sut.Cron.AddAsync(cron, AbortToken);
        sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);

        await sut.Coordinator!.CommitAsync();
        await _StartWorkerAsync(sut);
        await notified.Task.WaitAsync(_WaitTimeout, AbortToken);

        cron.ReconciledThroughUtc.Should().Be(_StoreAnchorUtc);
        sut.Scheduler.Received(1).RestartIfNeeded(new DateTime(2031, 3, 5, 0, 0, 0, DateTimeKind.Utc));
    }

    private static CronJobEntity _CronJob()
    {
        return new()
        {
            Id = Guid.NewGuid(),
            Function = _FunctionName,
            Description = _FunctionName,
            // CronScheduleCache parses with IncludingSeconds = true, so the expression has six fields.
            Expression = "0 0 0 * * *",
            Request = [],
        };
    }

    private enum CoordinatorMode
    {
        // No unit of work is begun in the scope: JobsManagerFacade resolves IUnitOfWorkManager.Current as null.
        None,

        // A resource-less unit of work (IUnitOfWorkManager.BeginAsync() with no db): Resource is null, so it is
        // treated exactly like "no unit of work" for the guarantee matrix (KD7) — coordination must not be
        // infectious to a scope that never opened a relational transaction.
        NonRelational,

        // An owned unit of work enlisting a live, open fake connection/transaction.
        LiveRelational,

        // An owned unit of work whose resource's connection reports Closed — the "incompatible/dead resource"
        // case, which the guarantee matrix (KD7) says throws regardless of TransactionEnlistment (except Never).
        DeadRelational,
    }

    private Sut _CreateSut(
        CoordinatorMode mode,
        bool withWriter,
        bool dispatcherEnabled = false,
        TimeProvider? timeProvider = null,
        IGuidGenerator? guidGenerator = null
    )
    {
        var persistence = withWriter
            ? Substitute.For<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>,
                ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>
            >()
            : Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;

        // Both cron creation paths are seeded writes: the provider supplies the STORE's anchor, the manager's seeder
        // derives the position from it, and the manager arms its restart from what came back. The stubs therefore run
        // the seeder for real against an anchor that is deliberately NOT this node's clock, so a manager that quietly
        // went back to its own TimeProvider shows up as a wrong assertion rather than as a passing default.
        persistence
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(_ApplySeed(call.Arg<CronJobEntity[]>(), call.Arg<CronSchedulePositionSeeder>()))
            );

        if (withWriter)
        {
            ((ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>)persistence)
                .WriteCronJobsAsync(
                    Arg.Any<CronJobEntity[]>(),
                    Arg.Any<CronSchedulePositionSeeder>(),
                    Arg.Any<IRelationalUnitOfWorkResource>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                    Task.FromResult(_ApplySeed(call.Arg<CronJobEntity[]>(), call.Arg<CronSchedulePositionSeeder>()))
                );
        }

        var scheduler = Substitute.For<IJobsHostScheduler>();
        var notification = Substitute.For<IJobsNotificationHubSender>();
        var dispatcher = Substitute.For<IJobsDispatcher>();
        dispatcher.IsEnabled.Returns(dispatcherEnabled);

        // A real IUnitOfWorkManager opened through the production factory (AddUnitOfWork), wrapped only so a test
        // can observe how many OnCompleted callbacks the manager registered and whether each completed
        // synchronously — mirrors the pre-existing CommitScopeProbe shape one-for-one.
        var unitOfWorkServices = new ServiceCollection().AddUnitOfWork().BuildServiceProvider();
        var unitOfWorkManager = unitOfWorkServices.GetRequiredService<IUnitOfWorkManager>();

        UnitOfWorkProbe? coordinator = mode switch
        {
            CoordinatorMode.None => null,
            CoordinatorMode.NonRelational => new UnitOfWorkProbe(
                unitOfWorkServices,
                _AwaitSync(unitOfWorkManager.BeginAsync())
            ),
            CoordinatorMode.LiveRelational => new UnitOfWorkProbe(
                unitOfWorkServices,
                _AwaitSync(
                    unitOfWorkManager.BeginAsync(
                        _ => ValueTask.FromResult<IUnitOfWorkResource>(new FakeRelationalResource(_LiveTransaction())),
                        options: null,
                        cancellationToken: default
                    )
                )
            ),
            CoordinatorMode.DeadRelational => new UnitOfWorkProbe(
                unitOfWorkServices,
                _AwaitSync(
                    unitOfWorkManager.BeginAsync(
                        _ =>
                            ValueTask.FromResult<IUnitOfWorkResource>(new FakeRelationalResource(_ClosedTransaction())),
                        options: null,
                        cancellationToken: default
                    )
                )
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        if (coordinator is not null)
        {
            _scopes.Add(coordinator);
        }
        else
        {
            unitOfWorkServices.Dispose();
        }

        var logger = new CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>>();
        var signalsLogger = new CapturingLogger<JobsPostCommitSignalService>();
        // Not started here: a test that needs the side effects to run calls _StartWorkerAsync, so every test can first
        // assert that the commit callback itself ran nothing.
        var signals = new JobsPostCommitSignalService(
            TestActivationBarrier.Opened(),
            effectiveTimeProvider,
            signalsLogger
        );
        _workers.Add(signals);

        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            persistence,
            scheduler,
            effectiveTimeProvider,
            guidGenerator ?? new SequentialGuidGenerator(SequentialGuidType.Version7),
            notification,
            new JobsExecutionContext(),
            dispatcher,
            new CronScheduleCache(TimeZoneInfo.Utc),
            signals,
            JobFunctionProvider.CreateHostRegistry(configuration: null),
            logger
        );

        return new Sut
        {
            Persistence = persistence,
            Scheduler = scheduler,
            Notification = notification,
            Dispatcher = dispatcher,
            Coordinator = coordinator,
            Manager = manager,
            Logger = logger,
            Signals = signals,
            SignalsLogger = signalsLogger,
        };
    }

    // Every fake resource factory completes synchronously (no real I/O), so the manager's ValueTask<IUnitOfWork>
    // is already complete by the time this returns — this is not a blocking wait on async work.
#pragma warning disable MA0045
    private static IUnitOfWork _AwaitSync(ValueTask<IUnitOfWork> pending) => pending.GetAwaiter().GetResult();
#pragma warning restore MA0045

    private static Task _StartWorkerAsync(Sut sut)
    {
        return sut.Signals.StartAsync(AbortToken);
    }

    // Notify is the last step of every time-job side-effect path, so its completion is the deterministic "the worker
    // processed the signal" hook without polling the mocks.
    private static TaskCompletionSource _NotifiedTimeJob(Sut sut)
    {
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Notification.AddTimeJobNotifyAsync(Arg.Any<Guid>())
            .Returns(_ =>
            {
                notified.TrySetResult();

                return Task.CompletedTask;
            });

        return notified;
    }

    private static TaskCompletionSource _NotifiedCronJob(Sut sut, CronJobEntity last)
    {
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Notification.AddCronJobNotifyAsync(last)
            .Returns(_ =>
            {
                notified.TrySetResult();

                return Task.CompletedTask;
            });

        return notified;
    }

    // Schedule-changed signals end in an unconditional Restart(), which is their completion hook.
    private static TaskCompletionSource _Restarted(Sut sut)
    {
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Scheduler.When(x => x.Restart()).Do(_ => restarted.TrySetResult());

        return restarted;
    }

    private static void _FillSignalChannel(Sut sut)
    {
        for (var i = 0; i < JobsPostCommitSignalService.Capacity; i++)
        {
            sut.Signals.TrySignal(TestPostCommitSignal.NoOp()).Should().BeTrue();
        }
    }

    private static IDisposable _ReplaceScheduleDispatch(
        Func<JobScheduleContext, JobScheduleNext, CancellationToken, Task> dispatch
    )
    {
        _BuildProvider(dispatch);
        return new ResetFunctionProvider();
    }

    private static void _BuildProvider(
        Func<JobScheduleContext, JobScheduleNext, CancellationToken, Task>? dispatch = null
    )
    {
        JobFunctionProvider.ResetForTests(discoveryComplete: false);
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [_FunctionName] = new JobFunctionRegistration
                {
                    CronExpression = "0 0 * * *",
                    Priority = JobPriority.LongRunning,
                    Delegate = (_, _, _) => Task.CompletedTask,
                    MaxConcurrency = 1,
                },
            }
        );

        if (dispatch is not null)
        {
            JobMiddlewareRegistry.RegisterSchedule("Tests:ScheduleDispatch", null, 0, dispatch.Invoke);
        }

        JobFunctionProvider.MarkDiscoveryComplete();
        JobFunctionProvider.Build();
    }

    private sealed class ResetFunctionProvider : IDisposable
    {
        public void Dispose() => JobFunctionProvider.ResetForTests();
    }

    // A store anchor far from any plausible node clock, so a store-anchored seed is unmistakable in an assertion.
    private static readonly DateTime _StoreAnchorUtc = new(2031, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private static CronSchedulePositionSeedResult _ApplySeed(CronJobEntity[] jobs, CronSchedulePositionSeeder seeder)
    {
        DateTime? earliest = null;

        foreach (var job in jobs)
        {
            var seed = seeder(job, _StoreAnchorUtc);
            job.ReconciledThroughUtc = seed.ReconciledThroughUtc;
            job.NextDueUtc = seed.NextDueUtc;
            job.EvaluationFingerprint = seed.EvaluationFingerprint;

            if (earliest is null || seed.NextDueUtc < earliest.Value)
            {
                earliest = seed.NextDueUtc;
            }
        }

        return new CronSchedulePositionSeedResult
        {
            StoreUtcNow = _StoreAnchorUtc,
            AffectedRows = jobs.Length,
            EarliestNextDueUtc = earliest,
        };
    }

    private sealed class Sut
    {
        public required IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence { get; init; }
        public required IJobsHostScheduler Scheduler { get; init; }
        public required IJobsNotificationHubSender Notification { get; init; }
        public required IJobsDispatcher Dispatcher { get; init; }
        public required UnitOfWorkProbe? Coordinator { get; init; }
        public required JobsManager<TimeJobEntity, CronJobEntity> Manager { get; init; }
        public required CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>> Logger { get; init; }
        public required JobsPostCommitSignalService Signals { get; init; }
        public required CapturingLogger<JobsPostCommitSignalService> SignalsLogger { get; init; }

        // KD5: the facade over the singleton core, wired with a fixed IUnitOfWorkManager stub reporting THIS
        // test's coordinator as Current — the same shape JobsManagerFacade consumes in production, just without
        // re-resolving per call (the tests below never change Current mid-flight, so a fixed value is equivalent).
        public ITimeJobManager<TimeJobEntity> Time =>
            new JobsManagerFacade<TimeJobEntity, CronJobEntity>(Manager, new FixedUnitOfWorkManager(Coordinator));

        public ICronJobManager<CronJobEntity> Cron =>
            new JobsManagerFacade<TimeJobEntity, CronJobEntity>(Manager, new FixedUnitOfWorkManager(Coordinator));

        public ICoordinatedJobWriter<TimeJobEntity, CronJobEntity> Writer =>
            (ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>)Persistence;
    }

    // A minimal IUnitOfWorkManager reporting a fixed Current — every other member is unused by JobsManagerFacade's
    // Add/keyed-schedule paths, which only ever read .Current.
    private sealed class FixedUnitOfWorkManager(IUnitOfWork? current) : IUnitOfWorkManager
    {
        public IUnitOfWork? Current { get; } = current;

        public ValueTask<IUnitOfWork> BeginAsync(
            UnitOfWorkOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("Not used by JobsManagerFacade.");

        public ValueTask<IUnitOfWork> BeginAsync(
            Func<CancellationToken, ValueTask<IUnitOfWorkResource>> beginResource,
            UnitOfWorkOptions? options,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Not used by JobsManagerFacade.");

        public IUnitOfWork Enlist(IUnitOfWorkResource resource, UnitOfWorkOptions? options = null) =>
            throw new NotSupportedException("Not used by JobsManagerFacade.");

        public IDisposable Adopt(IUnitOfWork unitOfWork) =>
            throw new NotSupportedException("Not used by JobsManagerFacade.");
    }

    private static DbTransaction _LiveTransaction()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        return new LiveTransaction(connection);
    }

    // The "incompatible/dead resource" case (KD7): a connection that reports Closed, which
    // CapturedRelationalResource.Validate() rejects regardless of TransactionEnlistment (except Never).
    private static DbTransaction _ClosedTransaction()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);
        return new LiveTransaction(connection);
    }

    private sealed class LiveTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;

        protected override DbConnection DbConnection => connection;

        public override void Commit() => throw new NotSupportedException();

        public override void Rollback() => throw new NotSupportedException();
    }

    private sealed class FakeRelationalResource(DbTransaction transaction) : IRelationalUnitOfWorkResource
    {
        public DbConnection Connection => Transaction.Connection!;

        public DbTransaction Transaction { get; } = transaction;

        public bool IsOwned => true;

        public bool IsTransactionCompleted => false;

        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    // A real IUnitOfWork opened through the production UnitOfWorkManager (AddUnitOfWork), wrapped only so a test can
    // observe how many OnCompleted callbacks the manager registered and whether each completed synchronously — the
    // wrapper never drives anything itself: CommitAsync/RollbackAsync forward to the real unit's own
    // CompleteAsync/RollbackAsync, exactly as a provider extension (BeginAsync/RunAsync) would drive them.
    private sealed class UnitOfWorkProbe(ServiceProvider services, IUnitOfWork inner) : IUnitOfWork, IAsyncDisposable
    {
        public int OnCommitCount { get; private set; }

        /// <summary>Commit callbacks whose returned <see cref="ValueTask" /> was already complete when it was returned.</summary>
        public int SynchronousCommitCallbacks { get; private set; }

        public UnitOfWorkState State => inner.State;

        public UnitOfWorkFailure? Failure => inner.Failure;

        public IUnitOfWorkResource? Resource => inner.Resource;

        public IRelationalUnitOfWorkResource? Relational => inner.Resource as IRelationalUnitOfWorkResource;

        public bool IsRetryPrevented => inner.IsRetryPrevented;

        public IDisposable OnCompleted(Func<ValueTask> work)
        {
            OnCommitCount++;

            return inner.OnCompleted(() =>
            {
                var pending = work();

                if (pending.IsCompleted)
                {
                    SynchronousCommitCallbacks++;
                }

                return pending;
            });
        }

        public IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work) => inner.OnFailed(work);

        public TState GetOrAdd<TState>(Func<IUnitOfWork, TState> factory)
            where TState : class => inner.GetOrAdd(factory);

        public TState GetOrAdd<TState, TArg>(TArg arg, Func<IUnitOfWork, TArg, TState> factory)
            where TState : class => inner.GetOrAdd(arg, factory);

        public void PreventRetry() => inner.PreventRetry();

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default) =>
            inner.CompleteAsync(cancellationToken);

        public ValueTask RollbackAsync() => inner.RollbackAsync();

        // Compat names matching the pre-existing probe surface used throughout this test file.
        public Task CommitAsync() => CompleteAsync().AsTask();

        public void Dispose() => inner.Dispose();

        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
