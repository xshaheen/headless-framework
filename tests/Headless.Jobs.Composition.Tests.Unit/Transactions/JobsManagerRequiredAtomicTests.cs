// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Text.Json;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Transactions;

public sealed partial class JobsManagerCoordinatedRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task required_atomic_keyed_and_batch_calls_reject_missing_relational_capability(bool nonRelational)
    {
        var middlewareCalls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                middlewareCalls++;
                return next(ct);
            }
        );
        var sut = _CreateSut(nonRelational ? CoordinatorMode.NonRelational : CoordinatorMode.None, withWriter: true);
        var refusal = _RefusalFor(nonRelational);
        var key = new JobKey("atomic-required");
        var candidate = _FutureTimeJob();
        candidate.Enlistment = TransactionEnlistment.Required;
        var schedule = () => sut.Time.ScheduleKeyedAsync(key, candidate, cancellationToken: AbortToken);
        await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal);
        var cancel = () =>
            sut.Time.CancelKeyedAsync(
                new JobKeyScope(_FunctionName),
                key,
                1,
                enlistment: TransactionEnlistment.Required,
                AbortToken
            );
        await cancel.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal);
        var root = _FutureTimeJob();
        root.Children.Add(candidate);
        var batch = () => sut.Time.AddBatchAsync([root], AbortToken);
        await batch.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal);
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
        candidate.Enlistment = TransactionEnlistment.Required;
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
        using var dispatch = _ReplaceScheduleDispatch(
            (context, next, ct) =>
            {
                ((TimeJobEntity)context.Job).Enlistment = TransactionEnlistment.Optional;
                return next(ct);
            }
        );
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var candidate = _FutureTimeJob();
        candidate.Enlistment = TransactionEnlistment.Required;
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
    public async Task required_keyed_cancellation_enlists_and_defers_restart()
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
        var result = await sut.Time.CancelKeyedAsync(
            scope,
            key,
            1,
            enlistment: TransactionEnlistment.Required,
            AbortToken
        );
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
    public async Task direct_provider_cannot_satisfy_required_atomicity_and_the_flag_is_not_payload()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var host = services.BuildServiceProvider();
        var provider = host.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var candidate = _FutureTimeJob();
        candidate.Enlistment = TransactionEnlistment.Required;
        JsonSerializer.Serialize(candidate).Should().NotContain(nameof(TimeJobEntity.Enlistment));
        var add = () => provider.AddTimeJobsAsync([candidate], AbortToken);
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("*direct persistence cannot satisfy*");
        var keyed = () =>
            provider.ScheduleKeyedTimeJobAsync(
                new JobKey("no-direct-fallback"),
                candidate,
                cancellationToken: AbortToken
            );
        await keyed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*direct persistence cannot satisfy*");
        (await provider.GetTimeJobByIdAsync(candidate.Id, AbortToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task required_atomic_cron_single_and_batch_calls_reject_missing_relational_capability(
        bool nonRelational
    )
    {
        var middlewareCalls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                middlewareCalls++;
                return next(ct);
            }
        );
        var sut = _CreateSut(nonRelational ? CoordinatorMode.NonRelational : CoordinatorMode.None, withWriter: true);
        var refusal = _RefusalFor(nonRelational);
        var required = _CronJob();
        required.Enlistment = TransactionEnlistment.Required;
        var add = () => sut.Cron.AddAsync(required, AbortToken);
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal);
        // One required definition makes the whole batch atomic-or-nothing.
        var batch = () => sut.Cron.AddBatchAsync([_CronJob(), required], AbortToken);
        await batch.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal);
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
    public async Task required_atomic_cron_batch_with_one_required_definition_enlists_the_whole_batch()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var required = _CronJob();
        required.Enlistment = TransactionEnlistment.Required;
        var result = await sut.Cron.AddBatchAsync([_CronJob(), required], AbortToken);
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

    [Fact]
    public async Task in_memory_provider_rejects_required_recurring_definitions_at_capture_and_the_flag_is_not_payload()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        await using var host = services.BuildServiceProvider();
        var manager = host.GetRequiredService<ICronJobManager<CronJobEntity>>();
        var required = _CronJob();
        required.Enlistment = TransactionEnlistment.Required;
        JsonSerializer.Serialize(required).Should().NotContain(nameof(CronJobEntity.Enlistment));
        // Capture runs before function validation, so the unregistered function never gets to fail first.
        var add = () => manager.AddAsync(required, AbortToken);
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a unit of work*");
        var batch = () => manager.AddBatchAsync([_CronJob(), required], AbortToken);
        await batch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires a unit of work*");
        var provider = host.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        (await provider.GetAllCronJobExpressionsAsync(AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public void atomic_flags_are_not_mapped_to_columns()
    {
        // JobsDbContext resolves its EF option builder from the application service provider (same shape as the
        // Sqlite EfFixture in Provider/TimeJobDeleteCascadeTests); only the model is inspected, no connection opens.
        using var services = new ServiceCollection()
            .AddEntityFrameworkSqlite()
            .AddSingleton(new JobsEfCoreOptionBuilder<TimeJobEntity, CronJobEntity>())
            .AddSingleton(new JobsStorageOptions())
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseApplicationServiceProvider(services)
            .Options;
        using var context = new JobsDbContext(options);
        context
            .Model.FindEntityType(typeof(CronJobEntity))!
            .FindProperty(nameof(CronJobEntity.Enlistment))
            .Should()
            .BeNull();
        context
            .Model.FindEntityType(typeof(TimeJobEntity))!
            .FindProperty(nameof(TimeJobEntity.Enlistment))
            .Should()
            .BeNull();
    }
}
