// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Jobs;
using Headless.UnitOfWork;

namespace Tests.Transactions;

public sealed partial class JobsManagerCoordinatedRoutingTests
{
    [Fact]
    public async Task enlisted_keyed_and_batch_calls_reject_a_unit_without_a_relational_resource()
    {
        var middlewareCalls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                middlewareCalls++;
                return next(ct);
            }
        );
        var sut = _CreateSut(CoordinatorMode.NonRelational, withWriter: true);
        var key = new JobKey("enlisted-keyed");
        var candidate = _FutureTimeJob();
        var schedule = () => sut.Time.ScheduleKeyedAsync(key, candidate, cancellationToken: AbortToken);
        await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage(_NoRelationalResourceRefusal);
        var cancel = () => sut.Time.CancelKeyedAsync(new JobKeyScope(_FunctionName), key, 1, AbortToken);
        await cancel.Should().ThrowAsync<InvalidOperationException>().WithMessage(_NoRelationalResourceRefusal);
        var root = _FutureTimeJob();
        root.Children.Add(candidate);
        var batch = () => sut.Time.AddBatchAsync([root], AbortToken);
        await batch.Should().ThrowAsync<InvalidOperationException>().WithMessage(_NoRelationalResourceRefusal);
        middlewareCalls.Should().Be(0);
        sut.Writer.DidNotReceive().ValidateContext(Arg.Any<IRelationalUnitOfWorkResource>(), Arg.Any<bool>());
        await sut
            .Persistence.DidNotReceive()
            .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task configured_writer_or_savepoint_rejection_precedes_middleware(bool keyed)
    {
        var calls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                calls++;
                return next(ct);
            }
        );
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        sut.Writer.When(writer => writer.ValidateContext(Arg.Any<IRelationalUnitOfWorkResource>(), keyed))
            .Do(_ => throw new NotSupportedException("configured capability rejected"));
        var candidate = _FutureTimeJob();
        Func<Task> write = keyed
            ? async () =>
                await sut.Time.ScheduleKeyedAsync(new JobKey("preflight"), candidate, cancellationToken: AbortToken)
            : async () => await sut.Time.AddAsync(candidate, AbortToken);
        await write.Should().ThrowAsync<NotSupportedException>();
        calls.Should().Be(0);
        sut.Coordinator!.OnCommitCount.Should().Be(0);
    }

    [Fact]
    public async Task captured_connection_must_stay_live_after_schedule_middleware()
    {
        Action? close = null;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                close!();
                return next(ct);
            }
        );
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var relational = sut.Coordinator!.Relational;
        relational.Should().NotBeNull();
        close = () => relational!.Connection.State.Returns(ConnectionState.Closed);
        var write = () => sut.Time.AddAsync(_FutureTimeJob(), AbortToken);
        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed*");
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator.OnCommitCount.Should().Be(0);
    }

    [Fact]
    public async Task keyed_results_are_provisional_and_restart_only_after_outer_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var candidate = _FutureTimeJob();
        sut.Writer.WriteKeyedTimeJobAsync(
                Arg.Any<JobKey>(),
                Arg.Any<TimeJobEntity>(),
                Arg.Any<long?>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new JobScheduleResult(
                JobScheduleDisposition.Created,
                call.Arg<TimeJobEntity>().Id,
                1,
                JobStatus.Idle
            ));
        var result = await sut.Time.ScheduleKeyedAsync(
            new JobKey("provisional"),
            candidate,
            cancellationToken: AbortToken
        );
        result.IsProvisional.Should().BeTrue();
        sut.Scheduler.DidNotReceive().Restart();
        sut.Coordinator!.OnCommitCount.Should().Be(1);
        var restarted = _Restarted(sut);
        await sut.Coordinator.CommitAsync();
        // The commit callback only queues the schedule-changed signal; the hosted worker issues the restart.
        sut.Scheduler.DidNotReceive().Restart();
        sut.Signals.PendingCount.Should().Be(1);
        await _StartWorkerAsync(sut);
        await restarted.Task.WaitAsync(_WaitTimeout, AbortToken);
        sut.Scheduler.Received(1).Restart();
        await sut
            .Persistence.DidNotReceive()
            .ScheduleKeyedTimeJobAsync(
                Arg.Any<JobKey>(),
                Arg.Any<TimeJobEntity>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task enlisted_keyed_cancellation_defers_restart_until_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var key = new JobKey("cancel-atomic");
        var scope = new JobKeyScope(_FunctionName);
        sut.Writer.CancelKeyedTimeJobAsync(
                scope,
                key,
                1,
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new JobScheduleResult(JobScheduleDisposition.Cancelled, Guid.NewGuid(), 1, JobStatus.Cancelled));
        var result = await sut.Time.CancelKeyedAsync(scope, key, 1, AbortToken);
        result.IsProvisional.Should().BeTrue();
        sut.Scheduler.DidNotReceive().Restart();
        var restarted = _Restarted(sut);
        await sut.Coordinator!.CommitAsync();
        sut.Scheduler.DidNotReceive().Restart();
        sut.Signals.PendingCount.Should().Be(1);
        await _StartWorkerAsync(sut);
        await restarted.Task.WaitAsync(_WaitTimeout, AbortToken);
        sut.Scheduler.Received(1).Restart();
    }

    [Fact]
    public async Task enlisted_cron_single_and_batch_calls_reject_a_unit_without_a_relational_resource()
    {
        var middlewareCalls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                middlewareCalls++;
                return next(ct);
            }
        );
        var sut = _CreateSut(CoordinatorMode.NonRelational, withWriter: true);
        var add = () => sut.Cron.AddAsync(_CronJob(), AbortToken);
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage(_NoRelationalResourceRefusal);
        var batch = () => sut.Cron.AddBatchAsync([_CronJob(), _CronJob()], AbortToken);
        await batch.Should().ThrowAsync<InvalidOperationException>().WithMessage(_NoRelationalResourceRefusal);
        middlewareCalls.Should().Be(0);
        sut.Writer.DidNotReceive().ValidateContext(Arg.Any<IRelationalUnitOfWorkResource>(), Arg.Any<bool>());
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task enlisted_cron_batch_writes_every_definition_in_one_coordinated_write()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var result = await sut.Cron.AddBatchAsync([_CronJob(), _CronJob()], AbortToken);
        result.Should().HaveCount(2);
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(jobs => jobs.Length == 2),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<IRelationalUnitOfWorkResource>(),
                Arg.Any<CancellationToken>()
            );
        await sut
            .Persistence.DidNotReceive()
            .InsertCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<CronSchedulePositionSeeder>(),
                Arg.Any<CancellationToken>()
            );
    }
}
