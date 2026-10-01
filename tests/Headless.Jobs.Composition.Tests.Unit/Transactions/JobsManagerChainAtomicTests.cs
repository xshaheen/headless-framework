// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.UnitOfWork;

namespace Tests.Transactions;

public sealed partial class JobsManagerCoordinatedRoutingTests
{
    [Fact]
    public async Task enlisted_chain_facade_refuses_a_unit_without_a_relational_resource_before_manager_effects()
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
        var (facade, chain) = _ChainFacade(sut);

        var enqueue = () => facade.EnqueueAsync(chain, AbortToken);
        await enqueue.Should().ThrowAsync<InvalidOperationException>().WithMessage(_NoRelationalResourceRefusal);

        middlewareCalls.Should().Be(0);
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
        sut.Scheduler.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
        await sut.Notification.DidNotReceiveWithAnyArgs().AddTimeJobNotifyAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task chain_facade_hands_the_whole_tree_to_one_write_chosen_by_the_receiver(bool coordinated)
    {
        var middlewareCalls = 0;
        using var dispatch = _ReplaceScheduleDispatch(
            (_, next, ct) =>
            {
                middlewareCalls++;
                return next(ct);
            }
        );
        var sut = _CreateSut(coordinated ? CoordinatorMode.LiveRelational : CoordinatorMode.None, withWriter: true);
        var (facade, chain) = _ChainFacade(sut);

        var id = await facade.EnqueueAsync(chain, AbortToken);

        id.Should().NotBeEmpty();
        middlewareCalls.Should().BePositive();

        if (coordinated)
        {
            await sut
                .Writer.Received(1)
                .WriteTimeJobsAsync(
                    Arg.Is<TimeJobEntity[]>(jobs => _IsWholeChain(jobs)),
                    Arg.Any<IRelationalUnitOfWorkResource>(),
                    Arg.Any<CancellationToken>()
                );
            await sut
                .Persistence.DidNotReceive()
                .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        }
        else
        {
            await sut
                .Persistence.Received(1)
                .AddTimeJobsAsync(Arg.Is<TimeJobEntity[]>(jobs => _IsWholeChain(jobs)), Arg.Any<CancellationToken>());
            await sut
                .Writer.DidNotReceive()
                .WriteTimeJobsAsync(
                    Arg.Any<TimeJobEntity[]>(),
                    Arg.Any<IRelationalUnitOfWorkResource>(),
                    Arg.Any<CancellationToken>()
                );
        }
    }

    // One root carrying both conditional children: the tree reaches storage in a single call, so its atomicity is
    // that call's (the unit's transaction when enlisted, the provider's own transaction when autonomous).
    private static bool _IsWholeChain(TimeJobEntity[] jobs) =>
        jobs is [{ Children.Count: 2 } root] && root.Children.All(child => child.ParentId == root.Id);

    private static (IJobScheduler Facade, JobChain Chain) _ChainFacade(Sut sut)
    {
        var registry = _BuildRegistry();
        var builder = JobChain.Start<RoutingJob>(DateTimeOffset.UtcNow.AddHours(1));
        builder.Root.Then<RoutingJob>();
        builder.Root.Catch<RoutingJob>();
        var facade = new JobScheduler<TimeJobEntity, CronJobEntity>(
            sut.Time,
            sut.Cron,
            registry,
            Substitute.For<IInternalJobManager>(),
            sut.Scheduler,
            new JobsRequestSerializationOptions(),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(),
            JobSchedulingPolicies.Empty
        );
        return (facade, builder.Build());
    }

    private sealed class RoutingJob : Headless.Jobs.Base.IJob
    {
        public ValueTask ExecuteAsync(Headless.Jobs.Base.JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
