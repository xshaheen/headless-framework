// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobExecutionTaskHandlerTests : TestBase
{
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
