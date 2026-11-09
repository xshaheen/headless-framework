// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A standalone time job whose retry delay exceeds the claim's one-second slack waits in the store, not in a worker:
/// the failed attempt is charged, the row goes back to <c>Idle</c> due after the delay, and the worker is free at once.
/// Shorter delays, cron occurrences, and chain nodes keep waiting in process.
/// </summary>
public sealed class JobsDurableRetryTests : TestBase
{
    private const string _Function = "durable.retry";
    private const string _OtherFunction = "durable.retry.other";
    private static readonly TimeSpan _RunBound = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task should_reschedule_a_long_retry_in_the_store_instead_of_waiting_in_the_worker()
    {
        // given
        await using var fixture = HandlerFixture.Create(rescheduled: 1);
        var context = fixture.Context(retryIntervals: [60]);

        // when: the bound is far below the 60 s delay, so the run must end without waiting it out
        await fixture
            .Handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken)
            .WaitAsync(_RunBound, AbortToken);

        // then
        fixture.Invocations.Should().Be(1);
        context.LeaseLost.Should().BeFalse();
        context.RetryCount.Should().Be(1);
        await fixture
            .Manager.Received(1)
            .RescheduleRetryAsync(context, 1, TimeSpan.FromSeconds(60), CancellationToken.None);
        await fixture
            .Manager.DidNotReceiveWithAnyArgs()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
        await fixture
            .ExceptionHandler.Received(1)
            .HandleExceptionAsync(
                Arg.Any<InvalidOperationException>(),
                context.JobId,
                JobType.TimeJob,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_end_as_lease_loss_when_the_reschedule_does_not_land(bool throws)
    {
        // given: the store faulted, or a peer reclaimed the row first
        await using var fixture = HandlerFixture.Create(rescheduled: throws ? null : 0);
        var context = fixture.Context(retryIntervals: [60]);

        // when
        await fixture
            .Handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken)
            .WaitAsync(_RunBound, AbortToken);

        // then: nothing reports an outcome the store never recorded
        context.LeaseLost.Should().BeTrue();
        await fixture
            .Manager.DidNotReceiveWithAnyArgs()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
        await fixture
            .ExceptionHandler.DidNotReceiveWithAnyArgs()
            .HandleExceptionAsync(default!, default, default, AbortToken);
    }

    [Theory]
    [InlineData(JobType.TimeJob, 1, false)]
    [InlineData(JobType.TimeJob, 60, true)]
    [InlineData(JobType.CronJobOccurrence, 60, false)]
    public async Task should_wait_in_process_when_the_retry_cannot_move_to_the_store(
        JobType type,
        int intervalSeconds,
        bool chainNode
    )
    {
        // given: a frozen clock, so a retry that waits in process never reaches its next attempt
        await using var fixture = HandlerFixture.Create(rescheduled: 1);
        var context = fixture.Context(retryIntervals: [intervalSeconds], type: type);
        if (chainNode)
        {
            context.ParentId = Guid.NewGuid();
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
#pragma warning disable AsyncFixer04 // False positive: the run is awaited below, before the token source is disposed.
        var execution = fixture.Handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: stop.Token);
#pragma warning restore AsyncFixer04

        // when: the failure was observed, which happens only once the in-process wait was planned
        await fixture.Observed.Task.WaitAsync(_RunBound, AbortToken);

        // then
        await fixture.Manager.DidNotReceiveWithAnyArgs().RescheduleRetryAsync(default!, default, default, AbortToken);
        await fixture.Manager.Received(1).UpdateTickerAsync(context, Arg.Any<CancellationToken>());
        fixture.Invocations.Should().Be(1);

        await stop.CancelAsync();
        await execution.WaitAsync(_RunBound, AbortToken);
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Retry)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    [InlineData(NodeDeathPolicy.Skip)]
    public async Task should_free_the_worker_during_the_delay_and_run_the_retry_when_it_is_due(NodeDeathPolicy policy)
    {
        // given: one worker, and a job whose first attempt fails with a 5 s retry delay, long enough for other work to
        // run inside it on a loaded runner
        var probe = new RetryProbe(failures: 1);
        using var host = _BuildHost(probe);
        await host.StartAsync(AbortToken);
        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var store = _Store(host);
            var beforeEnqueue = DateTime.UtcNow;
            var id = await scheduler.EnqueueAsync<RetryJob>(
                o => o.WithRetries(2).WithRetryIntervals(5).WithNodeDeathPolicy(policy),
                AbortToken
            );

            // when the first attempt fails
            var waiting = await _WaitForAsync(store, id, row => row.RetryCount == 1);

            // then the row waits in the store, by none of the node-death policies, with its attempt's lease released
            waiting.Status.Should().Be(JobStatus.Idle);
            waiting.Lease.State.Should().NotBe(RowLeaseState.Active);
            waiting.ExecutionTime.Should().BeOnOrAfter(beforeEnqueue.AddSeconds(5));

            // and the only worker runs other work meanwhile
            await scheduler.EnqueueAsync<OtherJob>(o => o.WithRetries(0), AbortToken);
            await probe.OtherRan.Task.WaitAsync(_RunBound, AbortToken);
            probe.Invocations.Should().Be(1);

            // and the retry runs once it is due
            var done = await _WaitForAsync(store, id, row => row.Status is JobStatus.Succeeded or JobStatus.DueDone);
            done.RetryCount.Should().Be(1);
            done.ExecutedAt.Should()
                .BeOnOrAfter(new DateTimeOffset(waiting.ExecutionTime!.Value.AddSeconds(-1), TimeSpan.Zero));
            probe.Invocations.Should().Be(2);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_run_a_rescheduled_retry_on_the_next_host_after_a_restart()
    {
        // given: the first host fails the first attempt and stops while the retry waits in the store
        var probe = new RetryProbe(failures: 1);
        using var first = _BuildHost(probe);
        await first.StartAsync(AbortToken);
        var store = _Store(first);
        var id = await first
            .Services.GetRequiredService<IJobScheduler>()
            .EnqueueAsync<RetryJob>(o => o.WithRetries(2).WithRetryIntervals(2), AbortToken);
        var waiting = await _WaitForAsync(store, id, row => row.RetryCount == 1);
        await first.StopAsync(AbortToken);

        // the stop leaves the waiting row as it was
        var stopped = await store.GetTimeJobByIdAsync(id, AbortToken);
        stopped!.Status.Should().Be(JobStatus.Idle);
        stopped.RetryCount.Should().Be(1);
        stopped.ExecutionTime.Should().Be(waiting.ExecutionTime);

        // when
        using var second = _BuildHost(probe, store);
        await second.StartAsync(AbortToken);
        try
        {
            // then the next host runs the remaining retry once, with the spent attempt still charged
            var done = await _WaitForAsync(store, id, row => row.Status is JobStatus.Succeeded or JobStatus.DueDone);
            done.RetryCount.Should().Be(1);
            probe.Invocations.Should().Be(2);
        }
        finally
        {
            await second.StopAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_fail_the_job_once_after_its_last_rescheduled_retry_fails()
    {
        // given: every attempt fails, and both retries wait in the store
        var probe = new RetryProbe(failures: int.MaxValue);
        var exhausted = 0;
        var exhaustedOnce = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = _BuildHost(
            probe,
            onExhausted: () =>
            {
                Interlocked.Increment(ref exhausted);
                exhaustedOnce.TrySetResult();
            }
        );
        await host.StartAsync(AbortToken);
        try
        {
            var store = _Store(host);
            var id = await host
                .Services.GetRequiredService<IJobScheduler>()
                .EnqueueAsync<RetryJob>(o => o.WithRetries(2).WithRetryIntervals(2), AbortToken);

            // when: the callback follows the stored Failed
            await exhaustedOnce.Task.WaitAsync(_RunBound, AbortToken);
            var failed = (await store.GetTimeJobByIdAsync(id, AbortToken))!;

            // then: the last retry still ran, then the budget ended the job
            failed.Status.Should().Be(JobStatus.Failed);
            failed.RetryCount.Should().Be(2);
            probe.Invocations.Should().Be(3);
            Volatile.Read(ref exhausted).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_run_a_rescheduled_retry_that_comes_due_while_the_failed_run_still_reports_its_exception()
    {
        // given: two workers, a delay just above the claim slack, and an exception handler that returns only once the
        // retry has started, so the retry is claimed while the failed run is still reporting its failure
        var probe = new RetryProbe(failures: 1);
        using var host = _BuildHost(probe, maxConcurrency: 2, exceptionHandler: new HoldUntilRetryStartsHandler(probe));
        await host.StartAsync(AbortToken);
        try
        {
            var store = _Store(host);
            var id = await host
                .Services.GetRequiredService<IJobScheduler>()
                .EnqueueAsync<RetryJob>(o => o.WithRetries(2).WithRetryIntervals(2), AbortToken);

            // when
            var done = await _WaitForAsync(store, id, row => row.Status is JobStatus.Succeeded or JobStatus.DueDone);

            // then: the retry ran instead of being dropped as a lost lease
            done.RetryCount.Should().Be(1);
            probe.Invocations.Should().Be(2);
        }
        finally
        {
            await host.StopAsync(AbortToken);
        }
    }

    private static IHost _BuildHost(
        RetryProbe probe,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null,
        Action? onExhausted = null,
        int maxConcurrency = 1,
        IJobExceptionHandler? exceptionHandler = null
    )
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(probe);
                services.AddHeadlessJobs(options =>
                    options
                        .AddModule<RetryJobsModule>()
                        .ConfigureRetries(retry =>
                            retry.OnExhausted = (_, _) =>
                            {
                                onExhausted?.Invoke();
                                return Task.CompletedTask;
                            }
                        )
                        .ConfigureScheduler(scheduler =>
                        {
                            scheduler.MaxConcurrency = maxConcurrency;
                            scheduler.ShutdownTimeout = TimeSpan.FromSeconds(2);
                        })
                );

                if (store is not null)
                {
                    services.AddSingleton(store);
                }

                if (exceptionHandler is not null)
                {
                    services.AddSingleton(exceptionHandler);
                }
            })
            .Build();
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static async Task<TimeJobEntity> _WaitForAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        Func<TimeJobEntity, bool> condition
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        budget.CancelAfter(_RunBound);

        while (true)
        {
            if (await store.GetTimeJobByIdAsync(id, budget.Token) is { } row && condition(row))
            {
                return row;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), budget.Token);
        }
    }

    private sealed class HandlerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private int _invocations;

        private HandlerFixture(
            ServiceProvider services,
            IInternalJobManager manager,
            IJobExceptionHandler exceptionHandler,
            JobsTaskScheduler scheduler,
            JobsExecutionTaskHandler handler,
            TaskCompletionSource observed
        )
        {
            _services = services;
            Manager = manager;
            ExceptionHandler = exceptionHandler;
            Scheduler = scheduler;
            Handler = handler;
            Observed = observed;
        }

        public IInternalJobManager Manager { get; }

        public IJobExceptionHandler ExceptionHandler { get; }

        public JobsTaskScheduler Scheduler { get; }

        public JobsExecutionTaskHandler Handler { get; }

        // Completes when the handler reports a failure to the consumer's exception handler.
        public TaskCompletionSource Observed { get; }

        public int Invocations => Volatile.Read(ref _invocations);

        // rescheduled: the row count the reschedule write returns, or null for a write that throws.
        public static HandlerFixture Create(int? rescheduled)
        {
            var manager = Substitute.For<IInternalJobManager>();
            manager
                .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(1));
            manager
                .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<bool?>(false));
            manager
                .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(1));
            manager
                .SettleInterruptedAsync(
                    Arg.Any<JobExecutionState>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.FromResult(1));
            manager
                .RescheduleRetryAsync(
                    Arg.Any<JobExecutionState>(),
                    Arg.Any<int>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    rescheduled is { } count
                        ? Task.FromResult(count)
                        : Task.FromException<int>(new TimeoutException("store unreachable"))
                );

            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exceptionHandler = Substitute.For<IJobExceptionHandler>();
            exceptionHandler
                .HandleExceptionAsync(
                    Arg.Any<Exception>(),
                    Arg.Any<Guid>(),
                    Arg.Any<JobType>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(_ =>
                {
                    observed.TrySetResult();
                    return Task.CompletedTask;
                });

            var registrations = new ServiceCollection();
            registrations.AddSingleton(manager);
            registrations.AddSingleton(exceptionHandler);
            var services = registrations.BuildServiceProvider();

            var identity = Substitute.For<IJobsOwnerIdentity>();
            identity.MembershipLostToken.Returns(CancellationToken.None);
            identity
                .TryGetStampOwner(out Arg.Any<string?>())
                .Returns(call =>
                {
                    call[0] = "node-a@1";
                    return true;
                });

            // A frozen clock: a retry that waits in process never reaches its next attempt during a test.
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
            var registry = JobFunctionRegistryBuilder.Build([], [], []);
            var handler = new JobsExecutionTaskHandler(
                services,
                clock,
                Substitute.For<IJobsInstrumentation>(),
                manager,
                registry,
                new JobsExecutionCancellationRegistry(),
                new SchedulerOptionsBuilder(),
                NullLogger<JobsExecutionTaskHandler>.Instance,
                new JobsFunctionTagBound(registry),
                taskScheduler: scheduler,
                ownerIdentity: identity
            );

            return new HandlerFixture(services, manager, exceptionHandler, scheduler, handler, observed);
        }

        public JobExecutionState Context(int[] retryIntervals, JobType type = JobType.TimeJob)
        {
            return new JobExecutionState
            {
                JobId = Guid.NewGuid(),
                FunctionName = _Function,
                Type = type,
                ExecutionTime = DateTime.UtcNow,
                RetryIntervals = retryIntervals,
                Retries = 3,
                RetryCount = 0,
                Status = JobStatus.InProgress,
                ClaimOwner = "node-a@1",
                CachedDelegate = (_, _, _) =>
                {
                    Interlocked.Increment(ref _invocations);
                    return Task.FromException(new InvalidOperationException("the attempt fails"));
                },
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    public sealed class RetryProbe(int failures)
    {
        private int _invocations;

        public TaskCompletionSource OtherRan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Invocations => Volatile.Read(ref _invocations);

        // Completes when the job function runs for the second time, i.e. when its first retry starts.
        public TaskCompletionSource RetryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync()
        {
            var invocation = Interlocked.Increment(ref _invocations);
            if (invocation >= 2)
            {
                RetryStarted.TrySetResult();
            }

            return invocation <= failures
                ? Task.FromException(new InvalidOperationException("the attempt fails"))
                : Task.CompletedTask;
        }
    }

    // A slow consumer handler, made deterministic: it is still running when the rescheduled retry is claimed.
    private sealed class HoldUntilRetryStartsHandler(RetryProbe probe) : IJobExceptionHandler
    {
        public async Task HandleExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        )
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(_RunBound);
            try
            {
                await probe.RetryStarted.Task.WaitAsync(bound.Token);
            }
            catch (OperationCanceledException)
            {
                // The retry never started; the test's own wait reports the failure.
            }
        }

        public Task HandleCanceledExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    public sealed class RetryJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class OtherJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class RetryJobsModule : IJobsModule
    {
        private RetryJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_Function] = new()
                    {
                        CronExpression = string.Empty,
                        Priority = JobPriority.Normal,
                        Delegate = static (services, _, _) => services.GetRequiredService<RetryProbe>().RunAsync(),
                        MaxConcurrency = 0,
                        JobType = typeof(RetryJob),
                    },
                    [_OtherFunction] = new()
                    {
                        CronExpression = string.Empty,
                        Priority = JobPriority.Normal,
                        Delegate = static (services, _, _) =>
                        {
                            services.GetRequiredService<RetryProbe>().OtherRan.TrySetResult();
                            return Task.CompletedTask;
                        },
                        MaxConcurrency = 0,
                        JobType = typeof(OtherJob),
                    },
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_Function] = new(_Function, null, string.Empty, JobPriority.Normal, 0),
                    [_OtherFunction] = new(_OtherFunction, null, string.Empty, JobPriority.Normal, 0),
                }
            );
        }
    }
}
