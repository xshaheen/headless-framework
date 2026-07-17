// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.Retry;

namespace Tests;

[Collection(nameof(JobsHelperCollection))]
public sealed class RetryBehaviorTests : TestBase
{
    // End-to-end unit tests that call the public ExecuteTaskAsync with a CronJobOccurrence
    // so RunContextFunctionAsync + retry logic is exercised. Tests use short intervals (1..3s).

    [Fact()]
    public async Task execute_task_async_cron_job_occurrence_applies_retry_intervals_and_updates_retry_count()
    {
        // given: cron occurrence -> RunContextFunctionAsync path
        // Use three distinct short intervals so we can verify mapping without overly long waits
        var (handler, context, _, attempts) = _SetupRetryTestFixture([1, 2, 3], retries: 3);

        // when
        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        // then - initial + 3 retries = 4 attempts
        attempts.Should().HaveCount(4);
        for (var i = 0; i < 4; i++)
        {
            attempts[i].RetryCount.Should().Be(i);
        }

        // Verify mapped retry intervals produced the expected spacing between attempts
        var timeDiffs = new[]
        {
            (attempts[1].Timestamp - attempts[0].Timestamp).TotalSeconds,
            (attempts[2].Timestamp - attempts[1].Timestamp).TotalSeconds,
            (attempts[3].Timestamp - attempts[2].Timestamp).TotalSeconds,
        };

        // Lower bound ensures the delay fired; upper bound is generous to tolerate CI/load jitter
        timeDiffs[0].Should().BeInRange(0.8, 2.5); // first retry uses ~1s
        timeDiffs[1].Should().BeInRange(1.5, 4.5); // second retry uses ~2s
        timeDiffs[2].Should().BeInRange(2.5, 6.5); // third retry uses ~3s
    }

    [Fact]
    public async Task execute_task_async_cron_job_occurrence_uses_last_interval_when_retries_exceed_array_length()
    {
        // Use zero intervals for speed
        var (handler, context, _, attempts) = _SetupRetryTestFixture([0, 0], retries: 4);

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        // initial + 4 retries = 5 attempts
        attempts.Should().HaveCount(5);

        // Ensure we captured attempts and they happened in order. Timing is intentionally tiny.
        attempts.Select(a => a.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task execute_task_async_cron_job_occurrence_stops_retrying_when_function_succeeds()
    {
        // given: succeed on RetryCount==2
        // Use zero intervals for speed; succeed at retry=2
        var (handler, context, _, attempts) = _SetupRetryTestFixture([0, 0, 0, 0], retries: 4, succeedOnRetryCount: 2);

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        // Should stop after success on attempt with RetryCount=2 => initial + retry1 + retry2 = 3 attempts
        attempts.Should().HaveCount(3);
        attempts[^1].RetryCount.Should().Be(2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public async Task execute_task_async_translates_retries_excluding_the_original_attempt(
        int retries,
        int expectedAttempts
    )
    {
        var options = _ZeroDelayRetryOptions();
        var (handler, context, manager, attempts) = _SetupRetryTestFixture([], retries, retryOptions: options);

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        attempts.Should().HaveCount(expectedAttempts);
        context.RetryCount.Should().Be(retries);
        context.Status.Should().Be(JobStatus.Failed);
        await manager
            .Received(retries + 1)
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_does_not_retry_or_exhaust_permanent_failures()
    {
        var exhausted = false;
        var options = _ZeroDelayRetryOptions();
        options.RetryStrategy.ShouldHandle = static args =>
            ValueTask.FromResult(args.Outcome.Exception is TimeoutException);
        options.OnExhausted = (_, _) =>
        {
            exhausted = true;
            return Task.CompletedTask;
        };
        var (handler, context, _, attempts) = _SetupRetryTestFixture(
            [],
            retries: 3,
            retryOptions: options,
            exceptionFactory: static () => new InvalidOperationException("permanent")
        );

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        attempts.Should().ContainSingle();
        context.RetryCount.Should().Be(0);
        context.Status.Should().Be(JobStatus.Failed);
        exhausted.Should().BeFalse();
    }

    [Fact]
    public async Task execute_task_async_persists_retry_count_before_the_next_attempt()
    {
        var options = _ZeroDelayRetryOptions();
        var (handler, context, manager, attempts) = _SetupRetryTestFixture([], retries: 1, retryOptions: options);
        var persistedBeforeSecondAttempt = false;
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var state = call.Arg<JobExecutionState>();
                if (state.RetryCount == 1 && state.Status == JobStatus.InProgress)
                {
                    persistedBeforeSecondAttempt = attempts.Count == 1;
                }

                return Task.FromResult(1);
            });

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        persistedBeforeSecondAttempt.Should().BeTrue();
        attempts.Select(x => x.RetryCount).Should().Equal(0, 1);
    }

    [Fact]
    public async Task execute_task_async_keeps_renewal_active_during_delay_and_fences_after_lease_loss()
    {
        var options = _ZeroDelayRetryOptions();
        var scheduler = new SchedulerOptionsBuilder
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            LeaseRenewalInterval = TimeSpan.FromMilliseconds(20),
        };
        var (handler, context, manager, attempts) = _SetupRetryTestFixture(
            [30],
            retries: 1,
            retryOptions: options,
            schedulerOptions: scheduler
        );
        manager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1), Task.FromResult(0));

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        attempts.Should().ContainSingle();
        context.LeaseLost.Should().BeTrue();
        context.Status.Should().Be(JobStatus.InProgress);
        await manager
            .DidNotReceive()
            .UpdateTickerAsync(
                Arg.Is<JobExecutionState>(state => state.Status == JobStatus.Failed),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task execute_task_async_resumes_from_the_durable_retry_count_without_resetting_the_budget()
    {
        var options = _ZeroDelayRetryOptions();
        var (handler, context, _, attempts) = _SetupRetryTestFixture([], retries: 2, retryOptions: options);
        context.RetryCount = 1;

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        attempts.Select(x => x.RetryCount).Should().Equal(1, 2);
        context.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task execute_task_async_uses_a_fresh_scope_and_observes_each_failure()
    {
        var exceptionHandler = Substitute.For<Headless.Jobs.Interfaces.IJobExceptionHandler>();
        var options = _ZeroDelayRetryOptions();
        var (handler, context, _, attempts) = _SetupRetryTestFixture(
            [],
            retries: 1,
            retryOptions: options,
            configureServices: services =>
            {
                services.AddScoped<RetryScopeMarker>();
                services.AddSingleton(exceptionHandler);
            }
        );
        var scopes = new List<RetryScopeMarker>();
        context.CachedDelegate = (provider, jobContext, _) =>
        {
            attempts.Add(new Attempt(DateTime.UtcNow, jobContext.RetryCount));
            scopes.Add(provider.GetRequiredService<RetryScopeMarker>());
            throw new TimeoutException("transient");
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        scopes.Should().HaveCount(2);
        scopes[0].Should().NotBeSameAs(scopes[1]);
        await exceptionHandler
            .Received(2)
            .HandleExceptionAsync(Arg.Any<Exception>(), context.JobId, context.Type, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_composes_cross_assembly_execute_middleware_once_per_attempt_and_observes_errors()
    {
        JobFunctionProvider.ResetForTests();
        try
        {
            var descriptor = new JobFunctionDescriptor("TestFunction", null, "", JobPriority.Normal, 0);
            JobFunctionProvider.RegisterFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [descriptor.FunctionName] = new()
                    {
                        CronExpression = string.Empty,
                        Priority = JobPriority.Normal,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                    },
                }
            );
            JobFunctionProvider.RegisterDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [descriptor.FunctionName] = descriptor,
                }
            );

            var attempts = new List<(int Attempt, RetryScopeMarker Scope, CancellationToken Token)>();
            var observedErrors = new List<string>();
            var terminalInvocations = 0;
            JobMiddlewareRegistry.RegisterExecute(
                "Consumer:ConsumerMiddleware",
                null,
                10,
                async (middlewareContext, next, token) =>
                {
                    try
                    {
                        attempts.Add(
                            (
                                middlewareContext.Attempt,
                                middlewareContext.Services.GetRequiredService<RetryScopeMarker>(),
                                token
                            )
                        );
                        await next(token);
                    }
                    catch (TimeoutException)
                    {
                        observedErrors.Add("consumer");
                        throw;
                    }
                }
            );
            JobMiddlewareRegistry.RegisterExecute(
                "Producer:ProducerMiddleware",
                null,
                -10,
                async (_, next, token) =>
                {
                    try
                    {
                        await next(token);
                    }
                    catch (TimeoutException)
                    {
                        observedErrors.Add("producer");
                        throw;
                    }
                }
            );
            JobFunctionProvider.Build();

            var options = _ZeroDelayRetryOptions();
            var (handler, context, manager, _) = _SetupRetryTestFixture(
                [],
                retries: 1,
                retryOptions: options,
                configureServices: static services => services.AddScoped<RetryScopeMarker>()
            );
            context.CachedDelegate = (_, functionContext, _) =>
            {
                terminalInvocations++;
                return functionContext.RetryCount == 0 ? throw new TimeoutException("transient") : Task.CompletedTask;
            };

            await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

            attempts.Select(attempt => attempt.Attempt).Should().Equal(0, 1);
            terminalInvocations.Should().Be(2);
            attempts[0].Scope.Should().NotBeSameAs(attempts[1].Scope);
            attempts.Should().OnlyContain(attempt => attempt.Token.CanBeCanceled);
            observedErrors.Should().Equal("consumer", "producer");
            context.Status.Should().Be(JobStatus.DueDone);
            await manager.Received(2).UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            JobFunctionProvider.ResetForTests();
        }
    }

    [Fact]
    public async Task execute_task_async_invokes_exhausted_callback_only_after_the_owned_terminal_write()
    {
        var terminalWriteObserved = false;
        var callbackObservedTerminalWrite = false;
        var options = _ZeroDelayRetryOptions();
        options.OnExhausted = (_, _) =>
        {
            callbackObservedTerminalWrite = terminalWriteObserved;
            return Task.CompletedTask;
        };
        var (handler, context, manager, _) = _SetupRetryTestFixture([], retries: 1, retryOptions: options);
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var state = call.Arg<JobExecutionState>();
                if (state.Status == JobStatus.Failed)
                {
                    terminalWriteObserved = true;
                }

                return Task.FromResult(1);
            });

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        callbackObservedTerminalWrite.Should().BeTrue();
    }

    [Fact]
    public async Task execute_task_async_does_not_invoke_exhausted_callback_after_a_stale_owner_write()
    {
        var callbackCount = 0;
        var options = _ZeroDelayRetryOptions();
        options.OnExhausted = (_, _) =>
        {
            callbackCount++;
            return Task.CompletedTask;
        };
        var (handler, context, manager, _) = _SetupRetryTestFixture([], retries: 1, retryOptions: options);
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<JobExecutionState>().Status == JobStatus.Failed ? 0 : 1));

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        callbackCount.Should().Be(0);
    }

    [Fact]
    public async Task execute_task_async_contains_exhausted_callback_failures_and_timeouts()
    {
        var callbackTokenCancelled = false;
        var options = _ZeroDelayRetryOptions();
        options.OnExhaustedTimeout = TimeSpan.FromMilliseconds(20);
        options.OnExhausted = (_, token) =>
        {
            token.Register(() => callbackTokenCancelled = true);
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        var (handler, context, _, _) = _SetupRetryTestFixture([], retries: 0, retryOptions: options);

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.Status.Should().Be(JobStatus.Failed);
        callbackTokenCancelled.Should().BeTrue();

        options.OnExhausted = static (_, _) => throw new InvalidOperationException("observer failure");
        var (throwingHandler, throwingContext, _, _) = _SetupRetryTestFixture([], retries: 0, retryOptions: options);
        await throwingHandler.ExecuteTaskAsync(throwingContext, isDue: true, cancellationToken: AbortToken);
        throwingContext.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task execute_task_async_bounds_a_hanging_per_retry_exception_observer()
    {
        // #6 (sibling of the OnExhausted bound): a slow / hanging IJobExceptionHandler.HandleExceptionAsync on the
        // per-retry path must not stall retry progression until lease loss. A tiny OnExhaustedTimeout cancels the
        // observer's linked token so a cooperative handler short-circuits and the retry proceeds to completion.
        var observerTokenCancelled = false;
        var exceptionHandler = Substitute.For<Headless.Jobs.Interfaces.IJobExceptionHandler>();
        exceptionHandler
            .HandleExceptionAsync(
                Arg.Any<Exception>(),
                Arg.Any<Guid>(),
                Arg.Any<JobType>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                token.Register(() => observerTokenCancelled = true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });

        var options = _ZeroDelayRetryOptions();
        options.OnExhaustedTimeout = TimeSpan.FromMilliseconds(20);
        var (handler, context, _, attempts) = _SetupRetryTestFixture(
            [],
            retries: 1,
            retryOptions: options,
            configureServices: services => services.AddSingleton(exceptionHandler)
        );

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        // Initial + 1 retry = 2 attempts: the hanging observer did not block the retry from running.
        attempts.Should().HaveCount(2);
        context.Status.Should().Be(JobStatus.Failed);
        // The observer's linked token was cancelled by the timeout bound so a cooperative handler can short-circuit.
        observerTokenCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task execute_task_async_stamps_child_ownership_before_running_child_delegate()
    {
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        await using var serviceProvider = services.BuildServiceProvider();

        var childId = Guid.NewGuid();
        var childOwned = false;
        internalManager
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.JobId == childId), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                childOwned = true;
                return Task.FromResult(1);
            });
        internalManager
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.JobId != childId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<JobExecutionState>().JobId != childId || childOwned ? 1 : 0));

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );
        var childInvoked = false;
        var parent = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "Parent",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            CachedDelegate = (_, _, _) => Task.CompletedTask,
        };
        parent.TimeJobChildren.Add(
            new JobExecutionState
            {
                JobId = childId,
                ParentId = parent.JobId,
                FunctionName = "Child",
                Type = JobType.TimeJob,
                RetryIntervals = [0],
                RunCondition = RunCondition.OnSuccess,
                Status = JobStatus.Idle,
                CachedDelegate = (_, _, _) =>
                {
                    childInvoked = true;
                    return Task.CompletedTask;
                },
            }
        );

        await handler.ExecuteTaskAsync(parent, isDue: false, cancellationToken: AbortToken);

        childInvoked.Should().BeTrue();
        await internalManager
            .Received()
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.JobId == childId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_marks_cooperative_base_operation_cancellation_as_cancelled()
    {
        var (handler, context, manager, _) = _SetupRetryTestFixture([0], retries: 0);
        context.CachedDelegate = (_, functionContext, cancellationToken) =>
        {
            functionContext.RequestCancellation();
            throw new OperationCanceledException(cancellationToken);
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.Status.Should().Be(JobStatus.Cancelled);
        await manager
            .Received(1)
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Cancelled), CancellationToken.None);
    }

    [Fact]
    public async Task execute_task_async_treats_foreign_operation_cancellation_as_failure()
    {
        var (handler, context, manager, _) = _SetupRetryTestFixture([0], retries: 0);
        context.CachedDelegate = (_, _, _) => throw new OperationCanceledException("provider timeout");

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.Status.Should().Be(JobStatus.Failed);
        await manager
            .Received(1)
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Failed), CancellationToken.None);
    }

    [Fact]
    public async Task execute_task_async_cancels_the_running_job_when_renewal_fails_with_a_db_outage()
    {
        // #463: a renewal that errors (DB unreachable) — or that cannot complete within the renewal cadence — must
        // trip cancel-on-loss for the in-flight job, not fault the renewal loop silently and leave the job running
        // while another node could reclaim the still-leased row.
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        // The first renewal proves ownership at start; the next renewal throws, simulating a DB outage mid-job.
        var renewalCalls = 0;
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                Interlocked.Increment(ref renewalCalls) == 1
                    ? Task.FromResult(1)
                    : Task.FromException<int>(new TimeoutException("simulated DB outage"))
            );

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder
            {
                LeaseDuration = TimeSpan.FromMinutes(5),
                LeaseRenewalInterval = TimeSpan.FromMilliseconds(100),
            },
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "LongJob",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
            // Runs until cancel-on-loss fires; the infinite delay observes the job token and throws on cancellation.
            CachedDelegate = async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct),
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        // #1: a lease-loss cancellation must NOT write a terminal status — the row is left InProgress for the
        // stalled-reclaim/OnNodeDeath sweep. So the job stops, LeaseLost is flagged, and no UpdateTicker write fires.
        context.LeaseLost.Should().BeTrue();
        context.Status.Should().NotBe(JobStatus.Cancelled);
        renewalCalls.Should().BeGreaterThanOrEqualTo(2);
        await internalManager
            .DidNotReceive()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_renewal_deadline_elapsing_trips_cancel_on_loss_without_terminalizing()
    {
        // #6/#463: exercises the DEADLINE branch of _TryRenewLeaseAsync (the per-cadence timeout CTS firing while the
        // renewal call hangs), distinct from the throw branch above. The blocking renewal is cancelled by the linked
        // timeout token, the job is cancelled on loss, and no terminal status is written (#1).
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        // The first renewal proves ownership at start; the next renewal hangs until its linked timeout token cancels.
        var renewalCalls = 0;
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref renewalCalls) == 1)
                {
                    return 1;
                }

                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());

                return 1;
            });

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder
            {
                LeaseDuration = TimeSpan.FromMinutes(5),
                LeaseRenewalInterval = TimeSpan.FromMilliseconds(100),
            },
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "LongJob",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
            CachedDelegate = async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct),
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.LeaseLost.Should().BeTrue();
        renewalCalls.Should().BeGreaterThanOrEqualTo(2);
        await internalManager
            .DidNotReceive()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_does_not_terminalize_when_delegate_ignores_lease_loss_cancellation()
    {
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();
        var renewalCalls = 0;
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Interlocked.Increment(ref renewalCalls) == 1 ? 1 : 0));

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder
            {
                LeaseDuration = TimeSpan.FromMinutes(5),
                LeaseRenewalInterval = TimeSpan.FromMilliseconds(20),
            },
            NullLogger<JobsExecutionTaskHandler>.Instance
        );
        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "CancellationIgnoringJob",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
        };
        context.CachedDelegate = async (_, _, _) =>
        {
            while (!context.LeaseLost)
            {
                await Task.Delay(5, AbortToken);
            }
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.LeaseLost.Should().BeTrue();
        context.Status.Should().Be(JobStatus.InProgress);
        await internalManager
            .DidNotReceive()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_does_not_invoke_delegate_when_start_lease_check_loses_ownership()
    {
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var logger = new CapturingLogger<JobsExecutionTaskHandler>();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder(),
            logger
        );

        var invoked = false;
        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "LostBeforeStart",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
            CachedDelegate = (_, _, _) =>
            {
                invoked = true;

                return Task.CompletedTask;
            },
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        invoked.Should().BeFalse();
        context.LeaseLost.Should().BeTrue();
        context.Status.Should().Be(JobStatus.InProgress);
        logger.Entries.Should().Contain(e => e.EventId == 3105);
        await internalManager
            .DidNotReceive()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_skips_the_tick_without_cancelling_when_renewal_reports_membership_unknown()
    {
        // #461: a negative RenewLeaseAsync result means coordination membership is momentarily unestablished, NOT a
        // lost lease. The renewal loop must skip the tick and let the healthy job keep running, not cancel it.
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        // Every renewal reports membership-unknown (sentinel < 0); the loop must skip, not cancel. The delegate blocks
        // until the SECOND renewal tick so the test is deterministic (no real-clock margins): by the 2nd call the 1st
        // tick's skip has already been logged, and we know the loop chose to keep running rather than cancel.
        var secondRenewalReached = new TaskCompletionSource();
        var renewalCalls = 0;
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref renewalCalls) >= 2)
                {
                    secondRenewalReached.TrySetResult();
                }

                return Task.FromResult(-1);
            });
        // Completion write applies (1) so the #462 reconciliation path is not triggered here.
        internalManager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var logger = new CapturingLogger<JobsExecutionTaskHandler>();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder
            {
                LeaseDuration = TimeSpan.FromMinutes(5),
                LeaseRenewalInterval = TimeSpan.FromMilliseconds(20),
            },
            logger
        );

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "LongJob",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
            // Completes only after the renewal loop has ticked twice — deterministic, not timing-dependent.
            CachedDelegate = async (_, _, ct) =>
                await secondRenewalReached.Task.WaitAsync(TimeSpan.FromSeconds(10), ct),
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.LeaseLost.Should().BeFalse(); // membership-unknown must NOT trip cancel-on-loss
        context.Status.Should().Be(JobStatus.DueDone);
        logger.Entries.Should().Contain(e => e.EventId == 3103); // membership-unknown skip was logged
    }

    [Fact]
    public async Task execute_task_async_cancels_on_loss_when_membership_stays_unknown_past_the_lease_window()
    {
        // #461 bound: a membership blip is tolerated only within the lease window. If membership stays unestablished
        // for the whole LeaseDuration (e.g. a permanent partition), the lease has lapsed and the row is being
        // reclaimed elsewhere — the loop must stop the local zombie via cancel-on-loss (leaving the row for the sweep).
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        // Membership never re-establishes — every renewal reports membership-unknown.
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(-1));

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder
            {
                LeaseDuration = TimeSpan.FromMilliseconds(250),
                LeaseRenewalInterval = TimeSpan.FromMilliseconds(40),
            },
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "LongJob",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
            // Runs until cancel-on-loss fires — deterministic: the lease-window bound WILL trip once membership has
            // been unknown for LeaseDuration (load only delays the moment, never changes the outcome).
            CachedDelegate = async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct),
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.LeaseLost.Should().BeTrue(); // bound tripped -> cancel-on-loss, no terminal write
        await internalManager
            .DidNotReceive()
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task execute_task_async_logs_reconciliation_when_a_successful_completion_write_is_fenced()
    {
        // #462: a job that completes successfully but whose completion write matches 0 rows (the row was reclaimed/
        // terminalized by a sweep after a stall) must log a reconciliation warning so operators don't treat the
        // recorded failure as real and re-trigger.
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();
        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        internalManager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        // The completion write is fenced out (0 rows) — the row was already terminalized by a sweep.
        internalManager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var logger = new CapturingLogger<JobsExecutionTaskHandler>();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            new SchedulerOptionsBuilder(),
            logger
        );

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "QuickJob",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Retries = 0,
            RetryCount = 0,
            Status = JobStatus.Idle,
            CachedDelegate = (_, _, _) => Task.CompletedTask, // succeeds immediately
        };

        await handler.ExecuteTaskAsync(context, isDue: true, cancellationToken: AbortToken);

        context.Status.Should().Be(JobStatus.DueDone); // local outcome is success...
        logger.Entries.Should().Contain(e => e.EventId == 3104); // ...but the fenced write is flagged for reconcile
    }

    // Minimal in-memory ILogger that records emitted entries so tests can assert a specific [LoggerMessage] fired.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, int EventId, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, eventId.Id, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed record Attempt(DateTime Timestamp, int RetryCount);

    // Helpers
    private static (
        JobsExecutionTaskHandler handler,
        JobExecutionState context,
        IInternalJobManager manager,
        List<Attempt> attempts
    ) _SetupRetryTestFixture(
        int[] retryIntervals,
        int retries,
        int? succeedOnRetryCount = null,
        JobsRetryOptions? retryOptions = null,
        Func<Exception>? exceptionFactory = null,
        Action<IServiceCollection>? configureServices = null,
        SchedulerOptionsBuilder? schedulerOptions = null
    )
    {
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();

        // The renewal loop cancels the job when RenewLeaseAsync returns 0 (lease lost). NSubstitute defaults a
        // Task<int> to 0, so without this stub every retry test is one renewal interval away from a spurious
        // cancel-on-loss. Return 1 ("lease held") so these tests exercise retry timing, not lease loss.
        internalManager
            .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        internalManager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        configureServices?.Invoke(services);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            instrumentation,
            internalManager,
            schedulerOptions ?? new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            retryOptions
        );

        var attempts = new List<Attempt>();

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "TestFunction",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = retryIntervals,
            Retries = retries,
            RetryCount = 0,
            Status = JobStatus.Idle,
            CachedDelegate = (sp, tctx, ct) =>
            {
                attempts.Add(new Attempt(DateTime.UtcNow, tctx.RetryCount));

                if (succeedOnRetryCount.HasValue && tctx.RetryCount >= succeedOnRetryCount.Value)
                {
                    return Task.CompletedTask;
                }

                throw exceptionFactory?.Invoke() ?? new InvalidOperationException("Fail for retry test");
            },
        };

        return (handler, context, internalManager, attempts);
    }

    private static JobsRetryOptions _ZeroDelayRetryOptions()
    {
        return new()
        {
            RetryStrategy = new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args =>
                    ValueTask.FromResult(args.Outcome.Exception is not null and not OperationCanceledException),
            },
        };
    }

    private sealed class RetryScopeMarker;
}
