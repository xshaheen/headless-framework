// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Execution;

/// <summary>
/// The moment a running job loses its lease logs at <c>Warning</c> with the reason, once per function per window, so a
/// partition that takes a whole function's runs logs one line instead of one per job.
/// </summary>
public sealed class JobsLeaseLossLoggingTests : TestBase
{
    // Event IDs are written out here, not read from the code under test, so a renumbering fails.
    private const int _LeaseLostEventId = 3130;
    private const int _LeaseLostSuppressedEventId = 3131;

    [Fact]
    public async Task should_log_one_warning_per_function_when_membership_loss_takes_several_running_jobs()
    {
        // given: two running jobs of one function and one of another, on a node about to lose its membership
        using var membershipLost = new CancellationTokenSource();
        var logger = new CapturingLogger();
        var handler = _Handler(logger, membershipLost.Token, "orders.sync", "invoices.send");
        var jobs = new[] { _Running("orders.sync"), _Running("orders.sync"), _Running("invoices.send") };
        var runs = jobs.Select(x => handler.ExecuteTaskAsync(x.Job, isDue: false, AbortToken)).ToArray();
        await Task.WhenAll(jobs.Select(x => x.Started.Task)).WaitAsync(AbortToken);

        // when
        await membershipLost.CancelAsync();
        await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        jobs.Should().AllSatisfy(x => x.Job.LeaseLost.Should().BeTrue());
        var warnings = logger.Of(_LeaseLostEventId);
        warnings.Should().HaveCount(2, "one warning per function within the window");
        warnings.Should().OnlyContain(x => x.Level == LogLevel.Warning && x.Message.Contains("MembershipLost"));
        warnings.Select(x => x.Message).Should().ContainSingle(x => x.Contains("orders.sync"));
        warnings.Select(x => x.Message).Should().ContainSingle(x => x.Contains("invoices.send"));
        var suppressed = logger.Of(_LeaseLostSuppressedEventId).Should().ContainSingle().Subject;
        suppressed.Level.Should().Be(LogLevel.Debug);
        suppressed.Message.Should().Contain("orders.sync");
    }

    private static (JobExecutionState Job, TaskCompletionSource Started) _Running(string function)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = function,
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            CachedDelegate = async (_, _, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };

        return (job, started);
    }

    private static JobsExecutionTaskHandler _Handler(
        ILogger<JobsExecutionTaskHandler> logger,
        CancellationToken membershipLost,
        params string[] functions
    )
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));

        var identity = Substitute.For<IJobsOwnerIdentity>();
        identity.MembershipLostToken.Returns(membershipLost);
        identity
            .TryGetStampOwner(out Arg.Any<string?>())
            .Returns(call =>
            {
                call[0] = "node-a@1";
                return true;
            });

        // Registered functions keep their own tag, so each keeps its own log window.
        var registry = JobFunctionRegistryBuilder.Build(
            [
                .. functions.Select(name => new KeyValuePair<string, JobFunctionRegistration>(
                    name,
                    new JobFunctionRegistration
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                    }
                )),
            ],
            [],
            []
        );
        var services = new ServiceCollection();
        services.AddSingleton(manager);

        return new JobsExecutionTaskHandler(
            services.BuildServiceProvider(),
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            logger,
            new JobsFunctionTagBound(registry),
            ownerIdentity: identity
        );
    }

    private sealed record Entry(int EventId, LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<JobsExecutionTaskHandler>
    {
        private readonly Lock _gate = new();
        private readonly List<Entry> _entries = [];

        public IReadOnlyList<Entry> Of(int eventId)
        {
            lock (_gate)
            {
                return [.. _entries.Where(x => x.EventId == eventId)];
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_gate)
            {
                _entries.Add(new Entry(eventId.Id, logLevel, formatter(state, exception)));
            }
        }
    }
}
