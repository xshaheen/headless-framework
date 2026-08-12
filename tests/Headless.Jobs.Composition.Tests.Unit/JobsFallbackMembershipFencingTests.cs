// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsFallbackMembershipFencingTests : TestBase
{
    [Fact]
    public async Task fallback_service_stops_sweeping_when_local_membership_is_lost()
    {
        // Fail-stop (R9): the fallback's reclaim sweep applies cluster-wide terminal transitions, so a node that
        // lost coordination membership must stop it. Under StopMembershipOnly nothing else stops this loop —
        // before the fix it kept terminalizing other live nodes' lease-lapsed rows indefinitely.
        var manager = Substitute.For<IInternalJobManager>();
        manager.ReclaimStalledResources(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        manager
            .RunTimedOutTickers(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));

        using var membershipLostCts = new CancellationTokenSource();
        var ownerIdentity = Substitute.For<IJobsOwnerIdentity>();
        ownerIdentity.MembershipLostToken.Returns(membershipLostCts.Token);

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        await using var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        using var service = new JobsFallbackBackgroundService(
            manager,
            new SchedulerOptionsBuilder { FallbackIntervalChecker = TimeSpan.FromMilliseconds(20) },
            handler,
            taskScheduler,
            new JobFunctionConcurrencyGate(),
            JobFunctionRegistryBuilder.Build([], [], []),
            TimeProvider.System,
            ownerIdentity,
            TestActivationBarrier.Opened(),
            NullLogger<JobsFallbackBackgroundService>.Instance
        );

        await service.StartAsync(AbortToken);

        // Wait until the loop has demonstrably ticked at least once.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (
            manager
                .ReceivedCalls()
                .All(c =>
                    !string.Equals(
                        c.GetMethodInfo().Name,
                        nameof(manager.ReclaimStalledResources),
                        StringComparison.Ordinal
                    )
                )
        )
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the fallback loop should tick while membership is intact");
            await Task.Delay(10, AbortToken);
        }

        await membershipLostCts.CancelAsync();

        var executeTask = service.ExecuteTask;
        executeTask.Should().NotBeNull();
        await executeTask.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        executeTask.IsCompleted.Should().BeTrue("membership loss must stop the fallback loop, not just host shutdown");

        await service.StopAsync(CancellationToken.None);
    }
}
