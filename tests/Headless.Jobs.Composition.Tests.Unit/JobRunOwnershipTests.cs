// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Managers;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// What a run stores when the node's ownership is in doubt, and when consumer callbacks run, through the real executor,
/// manager, and in-memory store: a run never starts or keeps running without a confirmed owner, and an exception or
/// cancellation callback can neither change nor delay the stored outcome.
/// </summary>
public sealed class JobRunOwnershipTests : TestBase
{
    private const string _Function = "ownership.job";
    private const string _ChildFunction = "ownership.job.child";

    // Only a failure guard: every scenario below ends on its own once the behavior holds.
    private static readonly TimeSpan _RunBound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_hand_the_row_back_uncharged_without_running_when_membership_is_unknown_at_start()
    {
        // given
        await using var harness = await Harness.CreateAsync(retries: 3, retryCount: 1);
        var context = await harness.ClaimAsync();
        harness.Identity.Forget();
        var invoked = false;

        // when
        await harness.ExecuteAsync(
            context,
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            runToken: AbortToken
        );

        // then
        invoked.Should().BeFalse();
        var stored = await harness.StoredAsync();
        stored.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
        stored.RetryCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_leave_the_row_running_for_reclaim_when_membership_is_lost_mid_run(bool runTokenFollowsLoss)
    {
        // given: the scheduler's run token is linked to the loss token; an immediate run's token is not
        await using var harness = await Harness.CreateAsync(retries: 3);
        var context = await harness.ClaimAsync();
        var claimed = await harness.StoredAsync();
        using var runToken = runTokenFollowsLoss
            ? CancellationTokenSource.CreateLinkedTokenSource(AbortToken, harness.Identity.MembershipLostToken)
            : CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // when
        var run = harness.ExecuteAsync(
            context,
            async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            runToken: runToken.Token
        );
        await started.Task.WaitAsync(_RunBound, AbortToken);
        await harness.Identity.LoseAsync();
        await run.WaitAsync(_RunBound, AbortToken);

        // then: no write at all, so the peer that reclaims the row charges the interrupted attempt
        context.LeaseLost.Should().BeTrue();
        var stored = await harness.StoredAsync();
        stored.Status.Should().Be(JobStatus.InProgress);
        stored.OwnerId.Should().Be(claimed.OwnerId);
        stored.RetryCount.Should().Be(claimed.RetryCount);
        stored.ExecutedAt.Should().BeNull();
    }

    [Fact]
    public async Task should_let_a_running_job_finish_when_membership_is_lost_while_the_host_drains()
    {
        // given
        await using var harness = await Harness.CreateAsync(retries: 3);
        var context = await harness.ClaimAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // when: the drain is already settling the run when the loss arrives
        var run = harness.ExecuteAsync(
            context,
            async (_, token) =>
            {
                started.SetResult();
                await proceed.Task.WaitAsync(token);
            },
            runToken: AbortToken
        );
        await started.Task.WaitAsync(_RunBound, AbortToken);
        harness.Scheduler.BeginDrain();
        await harness.Identity.LoseAsync();
        proceed.SetResult();
        await run.WaitAsync(_RunBound, AbortToken);

        // then
        context.LeaseLost.Should().BeFalse();
        (await harness.StoredAsync()).Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task should_let_an_in_progress_child_run_and_store_its_outcome_when_the_drain_begins_after_its_root_committed()
    {
        // given: the drain begins as the child confirms its owner, which is after its root's last drain check
        await using var harness = await Harness.CreateAsync(retries: 3, withChild: true);
        var context = await harness.ClaimAsync();
        harness.Identity.OnStamp = stamp =>
        {
            if (stamp == 2)
            {
                harness.Scheduler.BeginDrain();
            }
        };
        var childRan = false;

        // when
        await harness
            .ExecuteAsync(
                context,
                (_, _) => Task.CompletedTask,
                childBody: (_, _) =>
                {
                    childRan = true;
                    return Task.CompletedTask;
                },
                runToken: AbortToken
            )
            .WaitAsync(_RunBound, AbortToken);

        // then: a released non-timed child has no due instant to be claimed by, so it finishes with its root
        harness.Scheduler.IsDraining.Should().BeTrue();
        childRan.Should().BeTrue();
        context.TimeJobChildren[0].LeaseLost.Should().BeFalse();
        (await harness.StoredChildAsync()).Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.DueDone);
        (await harness.StoredAsync()).Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.DueDone);
    }

    [Fact]
    public async Task should_not_run_or_hand_back_a_child_when_membership_is_unknown_at_its_start()
    {
        // given: the identity is forgotten as the child confirms its owner, after its root started
        await using var harness = await Harness.CreateAsync(retries: 3, withChild: true);
        var context = await harness.ClaimAsync();
        harness.Identity.OnStamp = stamp =>
        {
            if (stamp == 2)
            {
                harness.Identity.Forget();
            }
        };
        var childRan = false;

        // when
        await harness
            .ExecuteAsync(
                context,
                (_, _) => Task.CompletedTask,
                childBody: (_, _) =>
                {
                    childRan = true;
                    return Task.CompletedTask;
                },
                runToken: AbortToken
            )
            .WaitAsync(_RunBound, AbortToken);

        // then: the child stays claimed with its root's tree for reclaim instead of being handed back on its own
        childRan.Should().BeFalse();
        var child = context.TimeJobChildren[0];
        child.LeaseLost.Should().BeTrue();
        var storedChild = await harness.StoredChildAsync();
        storedChild.Status.Should().Be(JobStatus.InProgress);
        storedChild.OwnerId.Should().Be(child.ClaimOwner);
        storedChild.ExecutedAt.Should().BeNull();
    }

    [Fact]
    public async Task should_not_start_an_in_progress_child_when_its_root_is_handed_back_before_it_starts()
    {
        // given: membership is unknown as the root confirms its owner and back by the time the child would
        await using var harness = await Harness.CreateAsync(retries: 3, withChild: true);
        var context = await harness.ClaimAsync();
        harness.Identity.OnStamp = stamp =>
        {
            if (stamp == 1)
            {
                harness.Identity.Forget();
            }
            else
            {
                harness.Identity.Remember();
            }
        };
        var rootRan = false;
        var childRan = false;

        // when
        await harness
            .ExecuteAsync(
                context,
                (_, _) =>
                {
                    rootRan = true;
                    return Task.CompletedTask;
                },
                childBody: (_, _) =>
                {
                    childRan = true;
                    return Task.CompletedTask;
                },
                runToken: AbortToken
            )
            .WaitAsync(_RunBound, AbortToken);

        // then: the next owner re-claims the root with its tree, so a child run here would run twice
        rootRan.Should().BeFalse();
        childRan.Should().BeFalse();
        (await harness.StoredAsync()).Status.Should().Be(JobStatus.Idle);
        var storedChild = await harness.StoredChildAsync();
        storedChild.Status.Should().NotBe(JobStatus.InProgress);
        storedChild.ExecutedAt.Should().BeNull();
    }

    [Fact]
    public async Task should_skip_an_in_progress_child_when_its_root_is_failed_by_crash_recovery_without_running()
    {
        // given: reclaims already consumed the root's whole retry budget
        await using var harness = await Harness.CreateAsync(retries: 0, retryCount: 1, withChild: true);
        var context = await harness.ClaimAsync();
        var rootRan = false;
        var childRan = false;

        // when
        await harness
            .ExecuteAsync(
                context,
                (_, _) =>
                {
                    rootRan = true;
                    return Task.CompletedTask;
                },
                childBody: (_, _) =>
                {
                    childRan = true;
                    return Task.CompletedTask;
                },
                runToken: AbortToken
            )
            .WaitAsync(_RunBound, AbortToken);

        // then: the root was never InProgress, so the child's condition never held
        rootRan.Should().BeFalse();
        childRan.Should().BeFalse();
        (await harness.StoredAsync()).Status.Should().Be(JobStatus.Failed);
        (await harness.StoredChildAsync()).Status.Should().Be(JobStatus.Skipped);
    }

    [Fact]
    public async Task should_store_failed_before_a_hanging_exception_handler_runs_and_keep_it_after_the_timeout()
    {
        // given
        var observedAtInvocation = new TaskCompletionSource<JobStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Harness? harnessRef = null;
        var exceptionHandler = new DelegatingExceptionHandler(onException: async _ =>
        {
            observedAtInvocation.TrySetResult((await harnessRef!.StoredAsync()).Status);
            // Ignores its token, so only the bound ends the wait.
            await hang.Task;
        });
        await using var harness = await Harness.CreateAsync(
            retries: 0,
            exceptionHandler: exceptionHandler,
            retryOptions: new JobsRetryOptions { OnExhaustedTimeout = TimeSpan.FromMilliseconds(50) }
        );
        harnessRef = harness;
        var context = await harness.ClaimAsync();

        // when
        await harness
            .ExecuteAsync(context, (_, _) => throw new InvalidOperationException("permanent"), runToken: AbortToken)
            .WaitAsync(_RunBound, AbortToken);

        // then
        (await observedAtInvocation.Task.WaitAsync(_RunBound, AbortToken))
            .Should()
            .Be(JobStatus.Failed);
        (await harness.StoredAsync()).Status.Should().Be(JobStatus.Failed);
        hang.SetResult();
    }

    [Fact]
    public async Task should_store_cancelled_and_contain_a_throwing_cancellation_handler()
    {
        // given
        var observedAtInvocation = new TaskCompletionSource<JobStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Harness? harnessRef = null;
        var exceptionHandler = new DelegatingExceptionHandler(onCanceled: async _ =>
        {
            observedAtInvocation.TrySetResult((await harnessRef!.StoredAsync()).Status);
            throw new InvalidOperationException("handler failure");
        });
        await using var harness = await Harness.CreateAsync(retries: 0, exceptionHandler: exceptionHandler);
        harnessRef = harness;
        var context = await harness.ClaimAsync();
        (await harness.RequestCancellationAsync()).Should().BeTrue();

        // when
        var run = async () =>
            await harness
                .ExecuteAsync(context, async (_, token) => await Task.Delay(Timeout.InfiniteTimeSpan, token))
                .WaitAsync(_RunBound, AbortToken);

        // then
        await run.Should().NotThrowAsync();
        (await observedAtInvocation.Task.WaitAsync(_RunBound, AbortToken)).Should().Be(JobStatus.Cancelled);
        (await harness.StoredAsync()).Status.Should().Be(JobStatus.Cancelled);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> _store;
        private readonly InternalJobsManager<TimeJobEntity, CronJobEntity> _manager;
        private readonly SchedulerOptionsBuilder _options;
        private readonly JobsRetryOptions? _retryOptions;
        private readonly TimeJobEntity _job;
        private readonly TimeJobEntity? _child;

        private Harness(
            ServiceProvider services,
            JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
            InternalJobsManager<TimeJobEntity, CronJobEntity> manager,
            SchedulerOptionsBuilder options,
            JobsRetryOptions? retryOptions,
            TimeJobEntity job,
            TimeJobEntity? child
        )
        {
            _services = services;
            _store = store;
            _manager = manager;
            _options = options;
            _retryOptions = retryOptions;
            _job = job;
            _child = child;
        }

        public FakeOwnerIdentity Identity { get; } = new();

        public JobsTaskScheduler Scheduler { get; } = new(maxConcurrency: 1, timeProvider: TimeProvider.System);

        public static async Task<Harness> CreateAsync(
            int retries,
            int retryCount = 0,
            IJobExceptionHandler? exceptionHandler = null,
            JobsRetryOptions? retryOptions = null,
            bool withChild = false
        )
        {
            var registrations = new ServiceCollection();
            registrations.AddSingleton(TimeProvider.System);
            registrations.AddHeadlessGuidGenerator();
            if (exceptionHandler is not null)
            {
                registrations.AddSingleton(exceptionHandler);
            }

            var services = registrations.BuildServiceProvider();
            var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
            var options = new SchedulerOptionsBuilder();
            var manager = new InternalJobsManager<TimeJobEntity, CronJobEntity>(
                store,
                TimeProvider.System,
                Substitute.For<IJobsNotificationHubSender>(),
                new CronScheduleCache(TimeZoneInfo.Utc),
                NullLogger<InternalJobsManager<TimeJobEntity, CronJobEntity>>.Instance,
                JobsRequestSerializationOptions.Default,
                services.GetRequiredService<IGuidGenerator>(),
                services,
                options
            );
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = _Function,
                ExecutionTime = DateTime.UtcNow,
                Retries = retries,
                RetryCount = retryCount,
                RetryIntervals = [0],
            };
            // A child that runs while its root runs, claimed with the root's tree.
            var child = withChild
                ? new TimeJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = _ChildFunction,
                    ParentId = job.Id,
                    RunCondition = RunCondition.InProgress,
                    Retries = 0,
                    RetryIntervals = [0],
                }
                : null;
            TimeJobEntity[] rows = child is null ? [job] : [job, child];
            (await store.AddTimeJobsAsync(rows, AbortToken)).Should().Be(rows.Length);
            return new Harness(services, store, manager, options, retryOptions, job, child);
        }

        public async Task<JobExecutionState> ClaimAsync()
        {
            var claimed = (await _store.AcquireImmediateTimeJobsAsync([_job.Id], AbortToken)).Single();
            Identity.Owner = claimed.OwnerId!;
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
                RetryIntervals = claimed.RetryIntervals,
                ClaimOwner = claimed.OwnerId,
            };

            if (_child is not null)
            {
                context.TimeJobChildren.Add(
                    new JobExecutionState
                    {
                        JobId = _child.Id,
                        ParentId = _child.ParentId,
                        FunctionName = _child.Function,
                        ContractVersion = _child.ContractVersion,
                        Type = JobType.TimeJob,
                        ExecutionTime = DateTime.UtcNow,
                        Status = _child.Status,
                        RunCondition = RunCondition.InProgress,
                        RetryCount = _child.RetryCount,
                        Retries = _child.Retries,
                        RetryIntervals = _child.RetryIntervals,
                        ClaimOwner = claimed.OwnerId,
                    }
                );
            }

            return context;
        }

        public Task<bool> RequestCancellationAsync() => _store.RequestTimeJobCancellationAsync(_job.Id, AbortToken);

        public async Task ExecuteAsync(
            JobExecutionState context,
            Func<JobContext, CancellationToken, Task> body,
            Func<JobContext, CancellationToken, Task>? childBody = null,
            CancellationToken runToken = default
        )
        {
            var registry = JobFunctionRegistryBuilder.Build(
                [
                    _Registration(_Function, body),
                    _Registration(_ChildFunction, childBody ?? ((_, _) => Task.CompletedTask)),
                ],
                [],
                [_Descriptor(_Function, context.ContractVersion), _Descriptor(_ChildFunction, context.ContractVersion)]
            );
            JobsExecutionContext.CacheFunctionReferences(context, registry);
            foreach (var child in context.TimeJobChildren)
            {
                JobsExecutionContext.CacheFunctionReferences(child, registry);
            }

            var handler = new JobsExecutionTaskHandler(
                _services,
                TimeProvider.System,
                Substitute.For<IJobsInstrumentation>(),
                _manager,
                registry,
                new JobsExecutionCancellationRegistry(),
                _options,
                NullLogger<JobsExecutionTaskHandler>.Instance,
                new JobsFunctionTagBound(registry),
                _retryOptions,
                taskScheduler: Scheduler,
                ownerIdentity: Identity
            );

            await handler.ExecuteTaskAsync(
                context,
                isDue: false,
                cancellationToken: runToken.CanBeCanceled ? runToken : AbortToken
            );
        }

        public async Task<TimeJobEntity> StoredAsync()
        {
            return (await _store.GetTimeJobByIdAsync(_job.Id, AbortToken))!;
        }

        public async Task<TimeJobEntity> StoredChildAsync()
        {
            return (await _store.GetTimeJobByIdAsync(_child!.Id, AbortToken))!;
        }

        private static KeyValuePair<string, JobFunctionRegistration> _Registration(
            string function,
            Func<JobContext, CancellationToken, Task> body
        )
        {
            return new(
                function,
                new()
                {
                    CronExpression = "",
                    Priority = JobPriority.Normal,
                    MaxConcurrency = 0,
                    Delegate = (_, execution, token) => body(execution, token),
                }
            );
        }

        private static KeyValuePair<string, JobFunctionDescriptor> _Descriptor(string function, string? version)
        {
            return new(function, new(function, null, "", JobPriority.Normal, 0, version));
        }

        public async ValueTask DisposeAsync()
        {
            Identity.Dispose();
            await Scheduler.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    // The coordinated identity's two observable states: known (yields the claim owner) and lost (yields nothing and
    // fires the loss token). Forgetting without the token models membership not yet re-established.
    private sealed class FakeOwnerIdentity : IJobsOwnerIdentity, IDisposable
    {
        private readonly CancellationTokenSource _lost = new();
        private volatile bool _known = true;
        private int _stamps;

        public string Owner { get; set; } = "node-a@1";

        public string DisplayOwner => Owner;

        public CancellationToken MembershipLostToken => _lost.Token;

        // Runs with each stamp's ordinal before it is answered, so a test can change the node's state at the moment a
        // given run confirms its owner: a root confirms first, and its children only once it has committed to run.
        public Action<int>? OnStamp { get; set; }

        public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
        {
            OnStamp?.Invoke(Interlocked.Increment(ref _stamps));
            owner = _known ? Owner : null;
            return owner is not null;
        }

        public void Forget() => _known = false;

        public void Remember() => _known = true;

        public Task LoseAsync()
        {
            _known = false;
            return _lost.CancelAsync();
        }

        public void Dispose() => _lost.Dispose();
    }

    private sealed class DelegatingExceptionHandler(
        Func<CancellationToken, Task>? onException = null,
        Func<CancellationToken, Task>? onCanceled = null
    ) : IJobExceptionHandler
    {
        public Task HandleExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        ) => onException?.Invoke(cancellationToken) ?? Task.CompletedTask;

        public Task HandleCanceledExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        ) => onCanceled?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }
}
