// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Coordination;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Pins the orphaned-owner sweep to the dead-owner bridge's cadence: the coordination retention window clamps it only
/// on the durable path (reclaimer registered) with a real coordination provider, the same inputs the bridge clamps on.
/// </summary>
public sealed class JobsFallbackOrphanSweepCadenceTests : TestBase
{
    private static readonly TimeSpan _configured = TimeSpan.FromMinutes(1);

    // Default CoordinationOptions keep a dead identity for 30 s, so the ceiling is half of that.
    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task should_clamp_the_orphan_sweep_cadence_when_the_reclaimer_runs_on_a_coordination_provider()
    {
        await using var harness = _Service(
            withReclaimer: true,
            Substitute.For<INodeMembership>(),
            Options.Create(new CoordinationOptions())
        );

        harness.Service.OrphanSweepInterval.Should().Be(_ceiling);
    }

    [Fact]
    public async Task should_keep_the_configured_cadence_when_membership_is_the_null_provider()
    {
        await using var harness = _Service(
            withReclaimer: true,
            new NullNodeMembership(),
            Options.Create(new CoordinationOptions())
        );

        harness.Service.OrphanSweepInterval.Should().Be(_configured);
    }

    [Fact]
    public async Task should_keep_the_configured_cadence_when_no_reclaimer_is_registered()
    {
        await using var harness = _Service(
            withReclaimer: false,
            Substitute.For<INodeMembership>(),
            Options.Create(new CoordinationOptions())
        );

        harness.Service.OrphanSweepInterval.Should().Be(_configured);
    }

    [Fact]
    public async Task should_keep_the_configured_cadence_when_no_coordination_options_are_registered()
    {
        await using var harness = _Service(withReclaimer: true, Substitute.For<INodeMembership>(), coordination: null);

        harness.Service.OrphanSweepInterval.Should().Be(_configured);
    }

    private static FallbackServiceHarness _Service(
        bool withReclaimer,
        INodeMembership membership,
        IOptions<CoordinationOptions>? coordination
    )
    {
        var manager = Substitute.For<IInternalJobManager>();
        var schedulerOptions = new SchedulerOptionsBuilder { DeadNodeReconcileInterval = _configured };
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var serviceProvider = services.BuildServiceProvider();
        var taskScheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            schedulerOptions,
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(JobFunctionRegistryBuilder.Build([], [], []))
        );

        return new FallbackServiceHarness(
#pragma warning disable CA2000 // FallbackServiceHarness takes ownership of the service and disposes it with the scheduler and provider.
            new JobsFallbackBackgroundService(
                manager,
                schedulerOptions,
                handler,
                taskScheduler,
                new JobFunctionConcurrencyGate(),
                JobFunctionRegistryBuilder.Build([], [], []),
                TimeProvider.System,
                Substitute.For<IJobsOwnerIdentity>(),
                TestActivationBarrier.Opened(),
                NullLogger<JobsFallbackBackgroundService>.Instance,
                membership,
                withReclaimer ? new JobsDeadOwnerReclaimer(manager, schedulerOptions) : null,
                coordination
            ),
#pragma warning restore CA2000
            taskScheduler,
            serviceProvider
        );
    }

    private sealed class FallbackServiceHarness(
        JobsFallbackBackgroundService service,
        JobsTaskScheduler taskScheduler,
        ServiceProvider serviceProvider
    ) : IAsyncDisposable
    {
        public JobsFallbackBackgroundService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await taskScheduler.DisposeAsync().ConfigureAwait(false);
            await serviceProvider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
