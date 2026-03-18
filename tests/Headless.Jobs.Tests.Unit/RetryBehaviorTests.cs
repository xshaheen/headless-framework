using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class RetryBehaviorTests
{
    // End-to-end unit tests that call the public ExecuteTaskAsync with a CronJobOccurrence
    // so RunContextFunctionAsync + retry logic is exercised. Tests use short intervals (1..3s).

    [Fact()]
    public async Task ExecuteTaskAsync_CronJobOccurrence_AppliesRetryIntervals_AndUpdatesRetryCount()
    {
        // given: cron occurrence -> RunContextFunctionAsync path
        // Use three distinct short intervals so we can verify mapping without overly long waits
        var (handler, context, _, attempts) = _SetupRetryTestFixture([1, 2, 3], retries: 3);

        // when
        await handler.ExecuteTaskAsync(context, isDue: true);

        // then - initial + 3 retries = 4 attempts
        attempts.Should().HaveCount(4);
        for (int i = 0; i < 4; i++)
            attempts[i].RetryCount.Should().Be(i);

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
    public async Task ExecuteTaskAsync_CronJobOccurrence_UsesLastInterval_WhenRetriesExceedArrayLength()
    {
        // Use zero intervals for speed
        var (handler, context, _, attempts) = _SetupRetryTestFixture([0, 0], retries: 4);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // initial + 4 retries = 5 attempts
        attempts.Should().HaveCount(5);

        // Ensure we captured attempts and they happened in order. Timing is intentionally tiny.
        attempts.Select(a => a.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ExecuteTaskAsync_CronJobOccurrence_StopsRetrying_WhenFunctionSucceeds()
    {
        // given: succeed on RetryCount==2
        // Use zero intervals for speed; succeed at retry=2
        var (handler, context, _, attempts) = _SetupRetryTestFixture([0, 0, 0, 0], retries: 4, succeedOnRetryCount: 2);

        await handler.ExecuteTaskAsync(context, isDue: true);

        // Should stop after success on attempt with RetryCount=2 => initial + retry1 + retry2 = 3 attempts
        attempts.Should().HaveCount(3);
        attempts.Last().RetryCount.Should().Be(2);
    }

    private sealed record Attempt(DateTime Timestamp, int RetryCount);

    // Helpers
    private static (
        JobsExecutionTaskHandler handler,
        InternalFunctionContext context,
        IInternalJobManager manager,
        List<Attempt> attempts
    ) _SetupRetryTestFixture(int[] retryIntervals, int retries, int? succeedOnRetryCount = null)
    {
        var services = new ServiceCollection();
        var internalManager = Substitute.For<IInternalJobManager>();
        var instrumentation = Substitute.For<IJobsInstrumentation>();

        services.AddSingleton(internalManager);
        services.AddSingleton(instrumentation);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new JobsExecutionTaskHandler(serviceProvider, TimeProvider.System, instrumentation, internalManager);

        var attempts = new List<Attempt>();

        var context = new InternalFunctionContext
        {
            JobId = Guid.NewGuid(),
            FunctionName = "TestFunction",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = retryIntervals,
            Retries = retries,
            RetryCount = 0,
            Status = JobStatus.Idle,
            CachedDelegate = (ct, sp, tctx) =>
            {
                attempts.Add(new Attempt(DateTime.UtcNow, tctx.RetryCount));

                if (succeedOnRetryCount.HasValue && tctx.RetryCount >= succeedOnRetryCount.Value)
                {
                    return Task.CompletedTask;
                }

                throw new InvalidOperationException("Fail for retry test");
            },
        };

        return (handler, context, internalManager, attempts);
    }
}
