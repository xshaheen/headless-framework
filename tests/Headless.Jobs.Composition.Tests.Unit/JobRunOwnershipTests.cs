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
            AbortToken
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
            runToken.Token
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
            AbortToken
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
            .ExecuteAsync(context, (_, _) => throw new InvalidOperationException("permanent"), AbortToken)
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

        private Harness(
            ServiceProvider services,
            JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store,
            InternalJobsManager<TimeJobEntity, CronJobEntity> manager,
            SchedulerOptionsBuilder options,
            JobsRetryOptions? retryOptions,
            TimeJobEntity job
        )
        {
            _services = services;
            _store = store;
            _manager = manager;
            _options = options;
            _retryOptions = retryOptions;
            _job = job;
        }

        public FakeOwnerIdentity Identity { get; } = new();

        public JobsTaskScheduler Scheduler { get; } = new(maxConcurrency: 1, timeProvider: TimeProvider.System);

        public static async Task<Harness> CreateAsync(
            int retries,
            int retryCount = 0,
            IJobExceptionHandler? exceptionHandler = null,
            JobsRetryOptions? retryOptions = null
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
            (await store.AddTimeJobsAsync([job], AbortToken)).Should().Be(1);
            return new Harness(services, store, manager, options, retryOptions, job);
        }

        public async Task<JobExecutionState> ClaimAsync()
        {
            var claimed = (await _store.AcquireImmediateTimeJobsAsync([_job.Id], AbortToken)).Single();
            Identity.Owner = claimed.OwnerId!;
            return new JobExecutionState
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
        }

        public Task<bool> RequestCancellationAsync() => _store.RequestTimeJobCancellationAsync(_job.Id, AbortToken);

        public async Task ExecuteAsync(
            JobExecutionState context,
            Func<JobContext, CancellationToken, Task> body,
            CancellationToken runToken = default
        )
        {
            var registry = JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        _Function,
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, execution, token) => body(execution, token),
                        }
                    ),
                ],
                [],
                [
                    new KeyValuePair<string, JobFunctionDescriptor>(
                        _Function,
                        new(_Function, null, "", JobPriority.Normal, 0, context.ContractVersion)
                    ),
                ]
            );
            JobsExecutionContext.CacheFunctionReferences(context, registry);
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

        public string Owner { get; set; } = "node-a@1";

        public string DisplayOwner => Owner;

        public CancellationToken MembershipLostToken => _lost.Token;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? owner)
        {
            owner = _known ? Owner : null;
            return owner is not null;
        }

        public void Forget() => _known = false;

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
