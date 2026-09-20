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
    [Theory]
    [InlineData("root", false)]
    [InlineData("success", false)]
    [InlineData("failure", false)]
    [InlineData("root", true)]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    public async Task chain_facade_preserves_required_atomicity_before_manager_effects(
        string requiredNode,
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
        var (facade, chain) = _ChainFacade(sut, requiredNode);

        var enqueue = () => facade.EnqueueAsync(chain, AbortToken);
        await enqueue.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal);

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
    public async Task chain_facade_without_required_atomicity_keeps_automatic_manager_routing(bool coordinated)
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
        var (facade, chain) = _ChainFacade(sut, requiredNode: null);

        var id = await facade.EnqueueAsync(chain, AbortToken);

        id.Should().NotBeEmpty();
        middlewareCalls.Should().BePositive();

        if (coordinated)
        {
            await sut
                .Writer.Received(1)
                .WriteTimeJobsAsync(
                    Arg.Any<TimeJobEntity[]>(),
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
                .AddTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
            await sut
                .Writer.DidNotReceive()
                .WriteTimeJobsAsync(
                    Arg.Any<TimeJobEntity[]>(),
                    Arg.Any<IRelationalUnitOfWorkResource>(),
                    Arg.Any<CancellationToken>()
                );
        }
    }

    private static (IJobScheduler Facade, JobChain Chain) _ChainFacade(Sut sut, string? requiredNode)
    {
        var registry = JobFunctionProvider.CreateHostRegistry(configuration: null);
        var descriptor = registry.Descriptors[_FunctionName];
        var builder = JobChain.Start(
            descriptor,
            DateTimeOffset.UtcNow.AddHours(1),
            new JobOptions { Enlistment = _Enlistment(requiredNode, "root") }
        );
        builder.Root.Then(descriptor, new JobOptions { Enlistment = _Enlistment(requiredNode, "success") });
        builder.Root.Catch(descriptor, new JobOptions { Enlistment = _Enlistment(requiredNode, "failure") });
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

    private static TransactionEnlistment _Enlistment(string? requiredNode, string candidate) =>
        string.Equals(requiredNode, candidate, StringComparison.Ordinal)
            ? TransactionEnlistment.Required
            : TransactionEnlistment.Optional;
}
