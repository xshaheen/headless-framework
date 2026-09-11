// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class JobExecutionTaskHandlerTests : TestBase
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task unsupported_version_releases_before_delegate_and_preserves_descendants(
        int affected,
        bool cachedMismatch
    )
    {
        var manager = _HealthyManager();
        manager
            .UpdateTickerAsync(
                Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Idle && x.ReleaseLock),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(affected));
        await using var services = new ServiceCollection().AddSingleton(manager).BuildServiceProvider();
        var ran = false;
        var childRan = false;
        var root = _Node("stable", () => ran = true);
        root.ContractVersion = "1";
        root.RetryCount = 3;
        root.Retries = 1;
        root.TimeJobChildren.Add(_Node("child", () => childRan = true, RunCondition.OnFailure));
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [],
            [
                new KeyValuePair<string, JobFunctionDescriptor>(
                    "stable",
                    new("stable", null, "", JobPriority.Normal, 0, "2")
                ),
            ]
        );
        if (cachedMismatch)
        {
            JobsExecutionContext.CacheFunctionReferences(root, registry);
        }
        var exhausted = false;
        var handler = new JobsExecutionTaskHandler(
            services,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsRetryOptions
            {
                OnExhausted = (_, _) =>
                {
                    exhausted = true;
                    return Task.CompletedTask;
                },
            }
        );

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        ran.Should().BeFalse("unsupported versions must be rejected before request deserialization in the delegate");
        root.ContractVersionError.Should().Contain("Unsupported stored Jobs contract");
        root.ExceptionDetails.Should().BeNull();
        root.ExecutedAt.Should().Be(default);
        root.ContractVersion.Should().Be("1");
        root.RetryCount.Should().Be(3);
        root.Status.Should().Be(JobStatus.Idle);
        root.ReleaseLock.Should().BeTrue();
        exhausted.Should().BeFalse("a node-local version gap must not exhaust the job's retry budget");
        root.LeaseLost.Should().Be(affected == 0);
        childRan.Should().BeFalse();
        await manager
            .Received(1)
            .UpdateTickerAsync(
                Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Idle && x.ReleaseLock),
                CancellationToken.None
            );
        await manager.DidNotReceive().ApplyParentTerminalRunConditionsAsync(root.JobId, Arg.Any<CancellationToken>());
        await manager
            .DidNotReceive()
            .UpdateSkipTimeJobsWithUnifiedContextAsync(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("2", "1")]
    public async Task incompatible_registry_preserves_stored_job_until_compatible_registry_executes(
        string storedVersion,
        string incompatibleVersion
    )
    {
        var time = new FakeTimeProvider();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(time);
        registrations.AddHeadlessGuidGenerator();
        await using var services = registrations.BuildServiceProvider();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var options = new SchedulerOptionsBuilder();
        var manager = new InternalJobsManager<TimeJobEntity, CronJobEntity>(
            store,
            time,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<TimeJobEntity, CronJobEntity>>.Instance,
            JobsRequestSerializationOptions.Default,
            services.GetRequiredService<IGuidGenerator>(),
            services,
            options
        );
        var job = JobsKeyedSchedulingScenarios.Candidate();
        job.ContractVersion = storedVersion;
        job.Retries = 0;
        (await store.AddTimeJobsAsync([job], AbortToken)).Should().Be(1);
        var invocations = 0;
        var exhausted = false;
        var retryOptions = new JobsRetryOptions
        {
            OnExhausted = (_, _) =>
            {
                exhausted = true;
                return Task.CompletedTask;
            },
        };

        async Task executeOnVersion(string version)
        {
            var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken)).Single();
            var context = new JobExecutionState
            {
                JobId = claimed.Id,
                FunctionName = claimed.Function,
                ContractVersion = claimed.ContractVersion,
                Type = JobType.TimeJob,
                ExecutionTime = claimed.ExecutionTime!.Value,
                Status = claimed.Status,
                RetryCount = claimed.RetryCount,
                Retries = claimed.Retries,
            };
            var registry = JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        job.Function,
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, execution, _) =>
                            {
                                version.Should().Be(storedVersion);
                                execution.ContractVersion.Should().Be(storedVersion);
                                invocations++;
                                return Task.CompletedTask;
                            },
                        }
                    ),
                ],
                [],
                [
                    new KeyValuePair<string, JobFunctionDescriptor>(
                        job.Function,
                        new(job.Function, null, "", JobPriority.Normal, 0, version)
                    ),
                ]
            );
            JobsExecutionContext.CacheFunctionReferences(context, registry);
            var handler = new JobsExecutionTaskHandler(
                services,
                time,
                Substitute.For<IJobsInstrumentation>(),
                manager,
                registry,
                new JobsExecutionCancellationRegistry(),
                options,
                NullLogger<JobsExecutionTaskHandler>.Instance,
                retryOptions
            );
            await handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken);
            context.LeaseLost.Should().BeFalse();
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await executeOnVersion(incompatibleVersion);
            var pending = (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!;
            pending.Status.Should().Be(JobStatus.Idle);
            pending.OwnerId.Should().BeNull();
            pending.LockedUntil.Should().BeNull();
            pending.ContractVersion.Should().Be(storedVersion);
            pending.Request.Should().Equal(job.Request!);
            pending.RetryCount.Should().Be(0);
            pending.ExecutedAt.Should().BeNull();
            pending.ExceptionMessage.Should().BeNull();
            invocations.Should().Be(0);
            exhausted.Should().BeFalse();
        }

        await executeOnVersion(storedVersion);
        var completed = (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        completed.Status.Should().Be(JobStatus.Succeeded);
        completed.ContractVersion.Should().Be(storedVersion);
        completed.Request.Should().Equal(job.Request!);
        completed.RetryCount.Should().Be(0);
        invocations.Should().Be(1);
        exhausted.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_preserve_parent_child_order_with_or_without_activity(bool activityEnabled)
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));

        var instrumentation = Substitute.For<IJobsInstrumentation>();
        var activityNames = new ConcurrentBag<string>();
        instrumentation
            .StartJobActivity(Arg.Do<string>(activityNames.Add), Arg.Any<JobExecutionState>())
            .Returns(_ => activityEnabled ? new Activity("job-test").Start() : null);

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddSingleton(instrumentation);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var parentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inProgressStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parentCompleted = false;
        var deferredObservedParentCompletion = false;
        var parent = _Job(
            "Parent",
            RunCondition.InProgress,
            async (_, _, _) =>
            {
                parentStarted.TrySetResult();
                await inProgressStarted.Task.ConfigureAwait(false);
                parentCompleted = true;
            }
        );
        parent.TimeJobChildren.Add(
            _Job(
                "Concurrent",
                RunCondition.InProgress,
                async (_, _, _) =>
                {
                    await parentStarted.Task.ConfigureAwait(false);
                    inProgressStarted.TrySetResult();
                }
            )
        );
        parent.TimeJobChildren.Add(
            _Job(
                "Deferred",
                RunCondition.OnSuccess,
                (_, _, _) =>
                {
                    deferredObservedParentCompletion = parentCompleted;
                    return Task.CompletedTask;
                }
            )
        );

        await handler.ExecuteTaskAsync(parent, isDue: false, cancellationToken: AbortToken);

        parentCompleted.Should().BeTrue();
        deferredObservedParentCompletion.Should().BeTrue();
        activityNames.Should().OnlyContain(name => name == "job.execute.timejob");
    }

    [Fact]
    public async Task executes_a_linear_five_deep_chain_in_order()
    {
        // U3/AE7 (in-memory executor half): the in-process recursion runs every descendant of a five-node chain in
        // parent-before-child order, past the old grandchild-level ceiling. A linear chain has one child per node, so
        // completion order is deterministic.
        var manager = _HealthyManager();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        var order = new List<string>();
        var root = _Node("root", () => order.Add("root"));
        var node = root;
        foreach (var name in new[] { "c1", "c2", "c3", "c4" })
        {
            var child = _Node(name, () => order.Add(name));
            node.TimeJobChildren.Add(child);
            node = child;
        }

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        order.Should().Equal("root", "c1", "c2", "c3", "c4");
    }

    [Fact]
    public async Task executes_all_deferred_children_beyond_the_static_sibling_buffer()
    {
        // KTD8: persisted data can carry more than the old fixed five-slot sibling buffer; every deferred child must
        // still run once the buffers become lists.
        var manager = _HealthyManager();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        var ran = new ConcurrentBag<string>();
        var root = _Node("root", () => { });
        for (var i = 0; i < 8; i++)
        {
            var name = $"child-{i}";
            root.TimeJobChildren.Add(_Node(name, () => ran.Add(name)));
        }

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        ran.Should().HaveCount(8);
    }

    [Fact]
    public async Task does_not_process_children_when_the_parent_loses_its_lease()
    {
        // KTD7: a lease-lost parent returns WITHOUT a terminal status (row left InProgress for the reclaim sweep). Its
        // children must be left unprocessed for reclaim — never wrongly Skipped by evaluating them against InProgress.
        var manager = _HealthyManager();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        var childRan = false;
        var root = _Node("root", () => { });
        root.TimeJobChildren.Add(_Node("child", () => childRan = true));

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        childRan.Should().BeFalse();
        await manager
            .DidNotReceive()
            .UpdateSkipTimeJobsWithUnifiedContextAsync(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task does_not_process_children_when_the_terminal_completion_write_is_fenced()
    {
        // KTD7: the parent runs to a local Succeeded status, but the completion write matches 0 rows — the row was
        // reclaimed/terminalized by a sweep and this status was never persisted (and may contradict the durable
        // record). Children must be left unprocessed (not run, not skipped) for reclaim, not driven from unpersisted
        // state.
        var manager = _HealthyManager();
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        var childRan = false;
        var root = _Node("root", () => { });
        root.TimeJobChildren.Add(_Node("child", () => childRan = true));

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        childRan.Should().BeFalse();
        await manager
            .DidNotReceive()
            .UpdateSkipTimeJobsWithUnifiedContextAsync(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task does_not_process_children_when_a_terminate_exception_completion_write_is_fenced()
    {
        // R7/KTD7: the parent throws TerminateExecutionException (a Skipped terminal), but its fenced terminal write in
        // the catch block matches 0 rows — the row was reclaimed/terminalized by a sweep. LeaseLost must be set and
        // the children left for reclaim (never driven from the unpersisted Skipped status).
        var manager = _HealthyManager();
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        var childRan = false;
        var root = _Job(
            "root",
            RunCondition.OnSuccess,
            (_, _, _) => throw new TerminateExecutionException("terminate mid-run")
        );
        root.TimeJobChildren.Add(_Node("child", () => childRan = true));

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        childRan.Should().BeFalse("a fenced terminate-completion write leaves children for reclaim, not execution");
        root.LeaseLost.Should().BeTrue("a 0-row terminal write in the catch block flags lease loss");
        // The catch-block terminal write is attempted under CancellationToken.None (survives graceful stop).
        await manager
            .Received()
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Skipped), CancellationToken.None);
        await manager
            .DidNotReceive()
            .UpdateSkipTimeJobsWithUnifiedContextAsync(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task does_not_process_children_when_a_durable_cancellation_completion_write_is_fenced()
    {
        // R7/KTD7: durable cancellation is observed mid-run and the job settles Cancelled, but its fenced terminal
        // write matches 0 rows (the row was reclaimed). LeaseLost must be set and the children left for reclaim.
        var manager = _HealthyManager();
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(true));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();

        // A fast observation interval so the durable-cancellation poll fires promptly against the blocking delegate.
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder { CancellationObservationInterval = TimeSpan.FromMilliseconds(10) },
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var childRan = false;
        var root = _Job(
            "root",
            RunCondition.OnSuccess,
            async (_, _, token) => await Task.Delay(Timeout.Infinite, token)
        );
        root.TimeJobChildren.Add(_Node("child", () => childRan = true));

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        childRan.Should().BeFalse("a fenced durable-cancellation write leaves children for reclaim, not execution");
        root.LeaseLost.Should()
            .BeTrue("a 0-row Cancelled write in the durable-cancellation catch block flags lease loss");
        root.Status.Should().Be(JobStatus.Cancelled);
    }

    [Fact]
    public async Task skips_the_entire_subtree_when_a_child_condition_is_not_met()
    {
        // A non-matching child skips with its whole subtree, at any depth (the skip gather is recursive).
        var manager = _HealthyManager();
        JobExecutionState[]? skipped = null;
        manager
            .UpdateSkipTimeJobsWithUnifiedContextAsync(
                Arg.Do<JobExecutionState[]>(argument => skipped = argument),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        // root succeeds; its OnFailure branch F -> G -> H must be skipped whole.
        var h = _Node("H", () => { });
        var g = _Node("G", () => { });
        g.TimeJobChildren.Add(h);
        var f = _Node("F", () => { }, RunCondition.OnFailure);
        f.TimeJobChildren.Add(g);
        var root = _Node("root", () => { });
        root.TimeJobChildren.Add(f);

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        skipped.Should().NotBeNull();
        skipped!.Select(x => x.FunctionName).Should().BeEquivalentTo(["F", "G", "H"]);
    }

    [Fact]
    public async Task processes_deferred_children_even_when_the_timed_child_reconcile_fails()
    {
        // Finding 1: the post-completion timed-descendant reconcile is a recoverable side-effect. If it throws AFTER
        // the parent's terminal write committed, the already-claimed non-timed deferred children must still be
        // processed — otherwise they strand Idle beneath a terminal root that nothing rediscovers while the node lives.
        var manager = _HealthyManager();
        manager
            .ApplyParentTerminalRunConditionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("reconcile boom")));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = _Handler(serviceProvider, manager);

        var childRan = false;
        var root = _Node("root", () => { });
        root.TimeJobChildren.Add(_Node("child", () => childRan = true));

        await handler.ExecuteTaskAsync(root, isDue: false, cancellationToken: AbortToken);

        childRan.Should().BeTrue("a failing timed-descendant reconcile must not strand the deferred non-timed child");
    }

    private static IInternalJobManager _HealthyManager()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        return manager;
    }

    private static JobsExecutionTaskHandler _Handler(IServiceProvider serviceProvider, IInternalJobManager manager)
    {
        return new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );
    }

    private static JobExecutionState _Node(
        string functionName,
        Action onRun,
        RunCondition runCondition = RunCondition.OnSuccess
    )
    {
        return _Job(
            functionName,
            runCondition,
            (_, _, _) =>
            {
                onRun();
                return Task.CompletedTask;
            }
        );
    }

    private static JobExecutionState _Job(string functionName, RunCondition runCondition, JobFunctionDelegate function)
    {
        return new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = functionName,
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = runCondition,
            CachedDelegate = function,
        };
    }
}
