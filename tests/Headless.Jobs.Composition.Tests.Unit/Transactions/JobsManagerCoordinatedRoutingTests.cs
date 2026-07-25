// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Transactions;

/// <summary>
/// Unit coverage for <see cref="JobsManager{TTimeJob,TCronJob}" /> commit-coordination routing: the synchronous
/// capture fork, the fail-loud cases, and post-commit side-effect deferral. Atomicity itself (rows committing /
/// discarding with the caller's transaction) is integration-only — see the EF harness conformance suite.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class JobsManagerCoordinatedRoutingTests : TestBase, IDisposable
{
    private const string _FunctionName = "routing-test-fn";

    public JobsManagerCoordinatedRoutingTests()
    {
        _BuildProvider();
    }

    public void Dispose() => JobFunctionProvider.ResetForTests();

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
            .Returns(new[] { acquired });
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
        TimeJobEntity job = FluentChainJobBuilder<TimeJobEntity>
            .BeginWith(parent => parent.SetFunction(_FunctionName).SetExecutionTime(now.UtcDateTime.AddHours(1)))
            .WithFirstChild(child => child.SetFunction(_FunctionName))
            .WithFirstGrandChild(grandChild => grandChild.SetFunction(_FunctionName));

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
            item.CreatedAt.Should().Be(now.UtcDateTime);
            item.UpdatedAt.Should().Be(now.UtcDateTime);
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
                    Arg.Any<IRelationalCommitContext>(),
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
                Arg.Any<IRelationalCommitContext>(),
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
                Arg.Any<IRelationalCommitContext>(),
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
            .InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
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
            .InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
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
                Arg.Any<DateTime>(),
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
                Arg.Any<IRelationalCommitContext>(),
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
            .InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
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
            .InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task time_job_live_coordinator_writes_in_transaction_and_defers_side_effects()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var job = _FutureTimeJob();

        var result = await sut.Time.AddAsync(job, AbortToken);

        result.Should().BeSameAs(job);
        await sut
            .Writer.Received(1)
            .WriteTimeJobsAsync(
                Arg.Is<TimeJobEntity[]>(a => a.Length == 1 && a[0].Id == job.Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Side effects must NOT have fired synchronously and the row must NOT have gone through the direct insert.
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddTimeJobNotifyAsync(Arg.Any<Guid>());

        await sut.Coordinator.DrainCommitAsync(AbortToken);

        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddTimeJobNotifyAsync(job.Id);
    }

    [Fact]
    public async Task deferred_side_effect_failure_is_swallowed_and_logged()
    {
        // KTD-4 crash isolation: once the row is durably committed, a deferred post-commit side-effect failure must
        // NOT propagate out of the commit drain (which would surface as a caller error after a successful commit) —
        // it is swallowed and logged against the job scope so the polling sweep can recover.
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var boom = new InvalidOperationException("deferred side effect boom");
        sut.Notification.AddTimeJobNotifyAsync(Arg.Any<Guid>()).Returns(Task.FromException(boom));

        await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        var drain = () => sut.Coordinator!.DrainCommitAsync(AbortToken);
        await drain.Should().NotThrowAsync();

        sut.Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && ReferenceEquals(e.Exception, boom));
    }

    [Fact]
    public async Task deferred_side_effect_timeout_is_swallowed_and_logged()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var timeout = TimeSpan.FromSeconds(5);
        var sut = _CreateSut(
            CoordinatorMode.LiveRelational,
            withWriter: true,
            dispatcherEnabled: true,
            timeProvider: timeProvider,
            postCommitDrainTimeout: timeout
        );
        var sideEffectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                sideEffectStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, callInfo.ArgAt<CancellationToken>(1));
                return [];
            });

        await sut.Time.AddAsync(_ImmediateTimeJob(), AbortToken);

        var drain = sut.Coordinator!.DrainCommitAsync(AbortToken);
        await sideEffectStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(timeout + TimeSpan.FromTicks(1));

        var drainAction = async () => await drain;
        await drainAction.Should().NotThrowAsync();
        sut.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
    }

    [Fact]
    public async Task deferred_side_effect_timeout_bounds_work_that_ignores_cancellation()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var timeout = TimeSpan.FromSeconds(5);
        var sut = _CreateSut(
            CoordinatorMode.LiveRelational,
            withWriter: true,
            dispatcherEnabled: true,
            timeProvider: timeProvider,
            postCommitDrainTimeout: timeout
        );
        var sideEffectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource<TimeJobEntity[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sideEffectStarted.SetResult();

                return neverCompletes.Task;
            });

        await sut.Time.AddAsync(_ImmediateTimeJob(), AbortToken);

        var drain = sut.Coordinator!.DrainCommitAsync(AbortToken);
        await sideEffectStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(timeout + TimeSpan.FromTicks(1));

        var drainAction = async () => await drain;
        await drainAction.Should().NotThrowAsync();
        neverCompletes.Task.IsCompleted.Should().BeFalse();
        sut.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);

        // Release the abandoned task after proving the drain did not retain it, so the late-completion continuation can
        // dispose its cancellation source before the test exits.
        neverCompletes.SetResult([]);
    }

    [Fact]
    public async Task deferred_side_effect_fault_after_timeout_is_observed_and_logged()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var timeout = TimeSpan.FromSeconds(5);
        var sut = _CreateSut(
            CoordinatorMode.LiveRelational,
            withWriter: true,
            dispatcherEnabled: true,
            timeProvider: timeProvider,
            postCommitDrainTimeout: timeout
        );
        var sideEffectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateSideEffect = new TaskCompletionSource<TimeJobEntity[]>();
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sideEffectStarted.SetResult();

                return lateSideEffect.Task;
            });

        await sut.Time.AddAsync(_ImmediateTimeJob(), AbortToken);

        var drain = sut.Coordinator!.DrainCommitAsync(AbortToken);
        await sideEffectStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(timeout + TimeSpan.FromTicks(1));
        await drain;

        var boom = new InvalidOperationException("late deferred side effect boom");
        lateSideEffect.SetException(boom);

        sut.Logger.Entries.Should().HaveCount(2);
        sut.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception == null);
        var lateFaultEntry = sut.Logger.Entries.Single(e => e.Exception != null);
        lateFaultEntry.Level.Should().Be(LogLevel.Warning);
        lateFaultEntry.Exception.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().Contain(boom);
    }

    [Fact]
    public async Task coordinated_enqueue_registers_no_rollback_callbacks()
    {
        // Coordinated side effects must fire only on commit — a rollback discards the row, so the manager must
        // never register a rollback callback (which would run side effects for work that was rolled back).
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);

        await sut.Time.AddAsync(_FutureTimeJob(), AbortToken);

        sut.Coordinator!.OnCommitCount.Should().Be(1);
        sut.Coordinator.OnRollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task time_job_live_coordinator_defers_immediate_dispatch_to_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: true);
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns([]);

        await sut.Time.AddAsync(_ImmediateTimeJob(), AbortToken);

        // The immediate-acquire probe is part of the deferred side effects, not the synchronous enqueue.
        await sut
            .Persistence.DidNotReceive()
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());

        await sut.Coordinator!.DrainCommitAsync(AbortToken);

        await sut
            .Persistence.Received(1)
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task time_job_batch_live_coordinator_routes_all_and_defers_side_effects_once()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };

        var result = await sut.Time.AddBatchAsync(jobs, AbortToken);

        result.Should().HaveCount(2);
        // R3: the batch reaches the seam as one array in insertion order (AddRange preserves it downstream).
        await sut
            .Writer.Received(1)
            .WriteTimeJobsAsync(
                Arg.Is<TimeJobEntity[]>(a => a.Length == 2 && a[0].Id == jobs[0].Id && a[1].Id == jobs[1].Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);
        await sut.Notification.DidNotReceive().AddTimeJobsBatchNotifyAsync();

        await sut.Coordinator.DrainCommitAsync(AbortToken);

        await sut.Notification.Received(1).AddTimeJobsBatchNotifyAsync();
    }

    [Fact]
    public async Task cron_without_coordinator_takes_direct_path()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);

        var result = await sut.Cron.AddAsync(_CronJob(), AbortToken);

        result.Should().NotBeNull();
        await sut.Persistence.Received(1).InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task cron_live_coordinator_writes_in_transaction_and_defers_cache_invalidation()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var cron = _CronJob();

        var result = await sut.Cron.AddAsync(cron, AbortToken);

        result.Should().BeSameAs(cron);
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Length == 1 && a[0].Id == cron.Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Cache invalidation + scheduler + notify must be deferred — never on a pre-commit snapshot.
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());

        await sut.Coordinator.DrainCommitAsync(AbortToken);

        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddCronJobNotifyAsync(cron);
    }

    [Fact]
    public async Task cron_batch_live_coordinator_routes_all_and_defers_side_effects_once()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };

        var result = await sut.Cron.AddBatchAsync(crons, AbortToken);

        result.Should().HaveCount(2);
        // R3: the batch reaches the seam as one array in insertion order (AddRange preserves it downstream).
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Length == 2 && a[0].Id == crons[0].Id && a[1].Id == crons[1].Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Cache invalidation + scheduler + per-entity notify must be deferred to commit, never on a pre-commit snapshot.
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddCronJobNotifyAsync(Arg.Any<CronJobEntity>());

        await sut.Coordinator.DrainCommitAsync(AbortToken);

        // Cache invalidation fires exactly once for the batch; scheduler restarts once; notify fires per entity.
        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        foreach (var cron in crons)
        {
            await sut.Notification.Received(1).AddCronJobNotifyAsync(cron);
        }
    }

    [Fact]
    public void jobs_only_host_resolves_manager_with_null_coordinator_fallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        using var provider = services.BuildServiceProvider();

        provider.GetService<ITimeJobManager<TimeJobEntity>>().Should().NotBeNull();
        provider.GetRequiredService<ICurrentCommitCoordinator>().Current.Should().BeNull();
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
        None,
        NonRelational,
        LiveRelational,
        DeadRelational,
    }

    private static Sut _CreateSut(
        CoordinatorMode mode,
        bool withWriter,
        bool dispatcherEnabled = false,
        TimeProvider? timeProvider = null,
        IGuidGenerator? guidGenerator = null,
        TimeSpan? postCommitDrainTimeout = null
    )
    {
        var persistence = withWriter
            ? Substitute.For<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>,
                ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>
            >()
            : Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var scheduler = Substitute.For<IJobsHostScheduler>();
        var notification = Substitute.For<IJobsNotificationHubSender>();
        var dispatcher = Substitute.For<IJobsDispatcher>();
        dispatcher.IsEnabled.Returns(dispatcherEnabled);

        var coordinator = mode switch
        {
            CoordinatorMode.None => null,
            CoordinatorMode.NonRelational => new FakeCommitCoordinator(relational: null),
            CoordinatorMode.LiveRelational => new FakeCommitCoordinator(
                new FakeRelationalCommitContext(Substitute.For<DbTransaction>())
            ),
            CoordinatorMode.DeadRelational => new FakeCommitCoordinator(
                new FakeRelationalCommitContext(transaction: null)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var logger = new CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>>();

        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            persistence,
            scheduler,
            timeProvider ?? TimeProvider.System,
            guidGenerator ?? new SequentialGuidGenerator(SequentialGuidType.Version7),
            notification,
            new JobsExecutionContext(),
            dispatcher,
            new FakeCurrentCommitCoordinator(coordinator),
            new CronScheduleCache(TimeZoneInfo.Utc),
            new SchedulerOptionsBuilder { PostCommitDrainTimeout = postCommitDrainTimeout ?? TimeSpan.FromSeconds(30) },
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
        };
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

    private sealed class Sut
    {
        public required IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence { get; init; }
        public required IJobsHostScheduler Scheduler { get; init; }
        public required IJobsNotificationHubSender Notification { get; init; }
        public required IJobsDispatcher Dispatcher { get; init; }
        public required FakeCommitCoordinator? Coordinator { get; init; }
        public required JobsManager<TimeJobEntity, CronJobEntity> Manager { get; init; }
        public required CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>> Logger { get; init; }

        public ITimeJobManager<TimeJobEntity> Time => Manager;

        public ICronJobManager<CronJobEntity> Cron => Manager;

        public ICoordinatedJobWriter<TimeJobEntity, CronJobEntity> Writer =>
            (ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>)Persistence;
    }

    private sealed class FakeCurrentCommitCoordinator(ICommitCoordinator? current) : ICurrentCommitCoordinator
    {
        public ICommitCoordinator? Current { get; } = current;
    }

    private sealed class FakeRelationalCommitContext(DbTransaction? transaction) : IRelationalCommitContext
    {
        public DbConnection? Connection => Transaction?.Connection;

        public DbTransaction? Transaction { get; } = transaction;
    }

    // Captures registered OnCommit callbacks so a test can assert deferral, then drive them to assert the deferred
    // work fires post-commit. Mirrors the real coordinator's "register now, drain after commit" contract.
    private sealed class FakeCommitCoordinator(IRelationalCommitContext? relational) : ICommitCoordinator
    {
        private readonly List<Func<CommitContext, CancellationToken, ValueTask>> _onCommit = [];
        private readonly List<Func<CommitContext, CancellationToken, ValueTask>> _onRollback = [];

        public int OnCommitCount => _onCommit.Count;

        public int OnRollbackCount => _onRollback.Count;

        public CommitCoordinatorState State => CommitCoordinatorState.Active;

        public IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work)
        {
            _onCommit.Add(work);

            return NoopDisposable.Instance;
        }

        public IDisposable OnRollback(Func<CommitContext, CancellationToken, ValueTask> work)
        {
            _onRollback.Add(work);

            return NoopDisposable.Instance;
        }

        public TBuffer GetOrAdd<TBuffer>(Func<ICommitCoordinator, TBuffer> factory)
            where TBuffer : class, ICommitWorkBuffer
        {
            return factory(this);
        }

        public TBuffer GetOrAdd<TBuffer, TState>(TState state, Func<ICommitCoordinator, TState, TBuffer> factory)
            where TBuffer : class, ICommitWorkBuffer
        {
            return factory(this, state);
        }

        public bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
            where TCapability : class, ICommitCapability
        {
            if (relational is TCapability typed)
            {
                capability = typed;

                return true;
            }

            capability = null;

            return false;
        }

        public async Task DrainCommitAsync(CancellationToken cancellationToken)
        {
            var context = new CommitContext
            {
                Services = EmptyServiceProvider.Instance,
                Outcome = CommitOutcome.Committed,
            };

            foreach (var work in _onCommit)
            {
                await work(context, cancellationToken);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    // Records the LoggerMessage-emitted entries so a test can assert the deferred-failure log without a logging mock.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
