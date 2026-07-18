// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class JobsExecutionCancellationRegistryTests : TestBase
{
    [Fact]
    public void registries_are_isolated_per_host()
    {
        var firstRegistry = new JobsExecutionCancellationRegistry();
        var secondRegistry = new JobsExecutionCancellationRegistry();
        using var source = new CancellationTokenSource();
        var registration = firstRegistry.Register(source, _Context(Guid.NewGuid()));

        secondRegistry.TrySignalDurableCancellation(registration).Should().BeFalse();
        source.IsCancellationRequested.Should().BeFalse();

        firstRegistry.TrySignalDurableCancellation(registration).Should().BeTrue();
        source.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void two_same_process_hosts_resolve_independent_registries()
    {
        using var firstHost = new HostBuilder()
            .ConfigureServices(static services => services.AddHeadlessJobs())
            .Build();
        using var secondHost = new HostBuilder()
            .ConfigureServices(static services => services.AddHeadlessJobs())
            .Build();
        var firstRegistry = firstHost.Services.GetRequiredService<JobsExecutionCancellationRegistry>();
        var secondRegistry = secondHost.Services.GetRequiredService<JobsExecutionCancellationRegistry>();
        using var source = new CancellationTokenSource();
        var registration = firstRegistry.Register(source, _Context(Guid.NewGuid()));

        secondRegistry.Should().NotBeSameAs(firstRegistry);
        secondRegistry.TrySignalDurableCancellation(registration).Should().BeFalse();
        source.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task one_host_observes_another_hosts_durable_cancellation_without_shared_runtime_state()
    {
        const string owner = "shared-node";
        var storeServices = new ServiceCollection();
        storeServices.AddSingleton<TimeProvider>(TimeProvider.System);
        storeServices.AddHeadlessGuidGenerator();
        storeServices.AddSingleton(new SchedulerOptionsBuilder { NodeId = owner });
        using var storeServiceProvider = storeServices.BuildServiceProvider();
        var durableStore = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(storeServiceProvider);

        using var executingHost = _CreateHost(durableStore);
        using var cancellingHost = _CreateHost(durableStore);
        executingHost
            .Services.GetRequiredService<JobsExecutionCancellationRegistry>()
            .Should()
            .NotBeSameAs(cancellingHost.Services.GetRequiredService<JobsExecutionCancellationRegistry>());
        var jobId = Guid.NewGuid();
        await durableStore.AddTimeJobsAsync(
            [
                new TimeJobEntity
                {
                    Id = jobId,
                    Function = "cross-host-cancellation",
                    Status = JobStatus.InProgress,
                    OwnerId = owner,
                    LockedUntil = DateTime.UtcNow.AddMinutes(1),
                },
            ],
            AbortToken
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new JobExecutionState
        {
            JobId = jobId,
            FunctionName = "cross-host-cancellation",
            Type = JobType.TimeJob,
            Status = JobStatus.InProgress,
            CachedDelegate = async (cancellationToken, _, _) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
        };

        var execution = executingHost
            .Services.GetRequiredService<JobsExecutionTaskHandler>()
            .ExecuteTaskAsync(context, isDue: false, AbortToken);
        await started.Task.WaitAsync(AbortToken);
        var accepted = await cancellingHost.Services.GetRequiredService<IJobScheduler>().CancelAsync(jobId, AbortToken);
        await execution.WaitAsync(AbortToken);

        accepted.Should().BeTrue();
        context.Status.Should().Be(JobStatus.Cancelled);
        var persisted = await durableStore.GetTimeJobByIdAsync(jobId, AbortToken);
        persisted!.Status.Should().Be(JobStatus.Cancelled);
        persisted.CancelRequested.Should().BeTrue();
    }

    [Fact]
    public void stale_handle_cannot_signal_or_remove_a_replacement_execution()
    {
        var registry = new JobsExecutionCancellationRegistry();
        var jobId = Guid.NewGuid();
        using var staleSource = new CancellationTokenSource();
        using var currentSource = new CancellationTokenSource();
        var stale = registry.Register(staleSource, _Context(jobId));
        var current = registry.Register(currentSource, _Context(jobId));

        registry.TrySignalDurableCancellation(stale).Should().BeFalse();
        registry.TryRemove(stale).Should().BeFalse();
        staleSource.IsCancellationRequested.Should().BeTrue();
        currentSource.IsCancellationRequested.Should().BeFalse();

        registry.TrySignalDurableCancellation(current).Should().BeTrue();
        current.Cause.Should().Be(JobsExecutionCancellationCause.DurableCancellation);
        currentSource.IsCancellationRequested.Should().BeTrue();
        registry.TryRemove(current).Should().BeTrue();
    }

    [Fact]
    public void first_cancellation_cause_wins_for_an_execution()
    {
        var registry = new JobsExecutionCancellationRegistry();
        using var source = new CancellationTokenSource();
        var registration = registry.Register(source, _Context(Guid.NewGuid()));

        registry.TrySignalHostShutdown(registration).Should().BeTrue();
        registry.TrySignalDurableCancellation(registration).Should().BeFalse();
        registry.TrySignalLeaseLoss(registration).Should().BeTrue();

        registration.Cause.Should().Be(JobsExecutionCancellationCause.LeaseLost);
        source.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void lease_loss_is_authoritative_after_durable_cancellation_or_shutdown()
    {
        var registry = new JobsExecutionCancellationRegistry();
        using var source = new CancellationTokenSource();
        var registration = registry.Register(source, _Context(Guid.NewGuid()));

        registry.TrySignalDurableCancellation(registration).Should().BeTrue();
        registry.TrySignalHostShutdown(registration).Should().BeFalse();
        registry.TrySignalLeaseLoss(registration).Should().BeTrue();

        registration.Cause.Should().Be(JobsExecutionCancellationCause.LeaseLost);
    }

    [Fact]
    public async Task concurrent_signal_and_removal_are_exact_and_disposal_safe()
    {
        var registry = new JobsExecutionCancellationRegistry();
        using var source = new CancellationTokenSource();
        var registration = registry.Register(source, _Context(Guid.NewGuid()));

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 32)
                .Select(index =>
                    Task.Run(() =>
                        index % 2 == 0
                            ? registry.TrySignalDurableCancellation(registration)
                            : registry.TryRemove(registration)
                    )
                )
        );

        results.Count(result => result).Should().BeInRange(1, 2);
        registry.TrySignalDurableCancellation(registration).Should().BeFalse();
        registry.TryRemove(registration).Should().BeFalse();

        source.Dispose();
        registry.Invoking(value => value.TrySignalDurableCancellation(registration)).Should().NotThrow();
    }

    [Fact]
    public async Task removal_waits_for_in_flight_signalling_before_the_execution_disposes_its_source()
    {
        var registry = new JobsExecutionCancellationRegistry();
        using var source = new CancellationTokenSource();
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var callback = source.Token.Register(() =>
        {
            callbackEntered.Set();
            releaseCallback.Wait();
        });
        var registration = registry.Register(source, _Context(Guid.NewGuid()));

        var signalTask = Task.Run(() => registry.TrySignalDurableCancellation(registration));
        callbackEntered.Wait(AbortToken);
        var removeTask = Task.Run(() => registry.TryRemove(registration));
        await Task.Delay(TimeSpan.FromMilliseconds(25), AbortToken);
        removeTask.IsCompleted.Should().BeFalse();

        releaseCallback.Set();
        (await signalTask).Should().BeTrue();
        (await removeTask).Should().BeTrue();
        source.Dispose();
        registry.TrySignalLeaseLoss(registration).Should().BeFalse();
    }

    [Fact]
    public void a_replacement_cannot_cross_an_exact_handle_that_is_committing_a_terminal_result()
    {
        var registry = new JobsExecutionCancellationRegistry();
        var jobId = Guid.NewGuid();
        using var completingSource = new CancellationTokenSource();
        using var rejectedSource = new CancellationTokenSource();
        var completing = registry.Register(completingSource, _Context(jobId));

        registry.TryBeginCompletion(completing).Should().BeTrue();
        var rejected = registry.Register(rejectedSource, _Context(jobId));

        registry.IsCurrent(completing).Should().BeTrue();
        registry.IsCurrent(rejected).Should().BeFalse();
        rejected.Cause.Should().Be(JobsExecutionCancellationCause.LeaseLost);
        rejectedSource.IsCancellationRequested.Should().BeTrue();
        registry.TryRemove(completing).Should().BeTrue();
    }

    [Fact]
    public void replacement_moves_the_parent_index_without_stale_cleanup_clobbering_it()
    {
        var registry = new JobsExecutionCancellationRegistry();
        var jobId = Guid.NewGuid();
        var staleParentId = Guid.NewGuid();
        var currentParentId = Guid.NewGuid();
        using var staleSource = new CancellationTokenSource();
        using var currentSource = new CancellationTokenSource();
        var stale = registry.Register(staleSource, _Context(jobId, staleParentId));
        var current = registry.Register(currentSource, _Context(jobId, currentParentId));

        registry.IsParentRunning(staleParentId).Should().BeFalse();
        registry.IsParentRunning(currentParentId).Should().BeTrue();
        registry.IsParentRunningExcludingSelf(currentParentId, jobId).Should().BeFalse();

        registry.TryRemove(stale).Should().BeFalse();
        registry.IsParentRunning(currentParentId).Should().BeTrue();
        registry.TryRemove(current).Should().BeTrue();
        registry.IsParentRunning(currentParentId).Should().BeFalse();
    }

    private static JobExecutionState _Context(Guid jobId, Guid? parentId = null) =>
        new()
        {
            JobId = jobId,
            ParentId = parentId,
            FunctionName = "TestFunction",
            Type = JobType.TimeJob,
        };

    private static IHost _CreateHost(IJobPersistenceProvider<TimeJobEntity, CronJobEntity> durableStore) =>
        new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddHeadlessJobs(options =>
                {
                    options.ConfigureScheduler(scheduler =>
                    {
                        scheduler.LeaseDuration = TimeSpan.FromSeconds(3);
                        scheduler.LeaseRenewalInterval = TimeSpan.FromSeconds(1);
                        scheduler.CancellationObservationInterval = TimeSpan.FromMilliseconds(25);
                    });
                });
                services.AddSingleton(durableStore);
            })
            .Build();
}
