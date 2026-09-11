// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Text.Json;
using Headless.CommitCoordination;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
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
        var key = new JobKey("atomic-required");
        var candidate = _FutureTimeJob();
        candidate.RequireAtomicEnlistment = true;
        var schedule = () => sut.Time.ScheduleKeyedAsync(key, candidate, cancellationToken: AbortToken);
        await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage("*atomic*");
        var cancel = () =>
            sut.Time.CancelKeyedAsync(
                new JobKeyScope(_FunctionName),
                key,
                1,
                requireAtomicEnlistment: true,
                AbortToken
            );
        await cancel.Should().ThrowAsync<InvalidOperationException>().WithMessage("*atomic*");
        var root = _FutureTimeJob();
        root.Children.Add(candidate);
        var batch = () => sut.Time.AddBatchAsync([root], AbortToken);
        await batch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*atomic*");
        middlewareCalls.Should().Be(0);
        sut.Writer.DidNotReceive().ValidateContext(Arg.Any<IRelationalCommitContext>(), Arg.Any<bool>());
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
        sut.Writer.When(writer => writer.ValidateContext(Arg.Any<IRelationalCommitContext>(), keyed))
            .Do(_ => throw new NotSupportedException("configured capability rejected"));
        var candidate = _FutureTimeJob();
        candidate.RequireAtomicEnlistment = true;
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
        sut.Coordinator!.TryGetCapability<IRelationalCommitContext>(out var relational).Should().BeTrue();
        close = () => relational!.Connection!.State.Returns(ConnectionState.Closed);
        var write = () => sut.Time.AddAsync(_FutureTimeJob(), AbortToken);
        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed*");
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
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
                ((TimeJobEntity)context.Job).RequireAtomicEnlistment = false;
                return next(ct);
            }
        );
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var candidate = _FutureTimeJob();
        candidate.RequireAtomicEnlistment = true;
        sut.Writer.WriteKeyedTimeJobAsync(
                Arg.Any<JobKey>(),
                Arg.Any<TimeJobEntity>(),
                Arg.Any<long?>(),
                Arg.Any<IRelationalCommitContext>(),
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
        await sut.Coordinator.DrainCommitAsync(AbortToken);
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
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new JobScheduleResult(JobScheduleDisposition.Cancelled, Guid.NewGuid(), 1, JobStatus.Cancelled));
        var result = await sut.Time.CancelKeyedAsync(scope, key, 1, requireAtomicEnlistment: true, AbortToken);
        result.IsProvisional.Should().BeTrue();
        sut.Scheduler.DidNotReceive().Restart();
        await sut.Coordinator!.DrainCommitAsync(AbortToken);
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
        candidate.RequireAtomicEnlistment = true;
        JsonSerializer.Serialize(candidate).Should().NotContain(nameof(TimeJobEntity.RequireAtomicEnlistment));
        var add = () => provider.AddTimeJobsAsync([candidate], AbortToken);
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("*atomic*");
        var keyed = () =>
            provider.ScheduleKeyedTimeJobAsync(
                new JobKey("no-direct-fallback"),
                candidate,
                cancellationToken: AbortToken
            );
        await keyed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*atomic*");
        (await provider.GetTimeJobByIdAsync(candidate.Id, AbortToken)).Should().BeNull();
    }
}
