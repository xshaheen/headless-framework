// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Polly;
using Polly.Retry;

namespace Tests;

public sealed class JobsRetryPipelineTests : TestBase
{
    [Fact]
    public async Task should_delegate_fixed_exponential_and_jittered_delays_to_polly()
    {
        var fixedDelays = await _CaptureDelaysAsync(DelayBackoffType.Constant, useJitter: false);
        fixedDelays.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        var exponentialDelays = await _CaptureDelaysAsync(DelayBackoffType.Exponential, useJitter: false);
        exponentialDelays.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

        var jittered = await _CaptureDelaysAsync(DelayBackoffType.Exponential, useJitter: true);
        jittered.Should().OnlyContain(delay => delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(5));
        jittered.Should().NotEqual(exponentialDelays);
    }

    [Fact]
    public async Task should_cap_custom_delay_generators()
    {
        var options = new JobsRetryOptions
        {
            RetryStrategy = new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                MaxDelay = TimeSpan.FromHours(1),
                ShouldHandle = static _ => ValueTask.FromResult(true),
                DelayGenerator = static _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.FromHours(48)),
            },
        };

        var capturedDelays = await _ExecuteToExhaustionAsync(options, retries: 2);

        capturedDelays.Should().Equal(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task should_preserve_polly_attempt_numbers_after_process_recovery()
    {
        var shouldHandleAttempts = new List<int>();
        var delayAttempt = -1;
        var observerAttempt = -1;
        var options = new JobsRetryOptions
        {
            RetryStrategy = new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                ShouldHandle = args =>
                {
                    shouldHandleAttempts.Add(args.AttemptNumber);
                    return ValueTask.FromResult(true);
                },
                DelayGenerator = args =>
                {
                    delayAttempt = args.AttemptNumber;
                    return ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);
                },
                OnRetry = args =>
                {
                    observerAttempt = args.AttemptNumber;
                    return ValueTask.CompletedTask;
                },
            },
        };
        var pipeline = new JobsRetryPipeline(options, TimeProvider.System, NullLogger.Instance);
        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "Recovered",
            Type = JobType.TimeJob,
            Retries = 2,
            RetryCount = 1,
        };

        var action = async () =>
            await pipeline.ExecuteAsync(
                job,
                static (_, _) => ValueTask.FromException(new TimeoutException()),
                static (_, _, _) => ValueTask.CompletedTask,
                static _ => { },
                AbortToken
            );

        await action.Should().ThrowAsync<TimeoutException>();
        shouldHandleAttempts.Should().Equal(0, 1);
        delayAttempt.Should().Be(0);
        observerAttempt.Should().Be(0);
    }

    [Fact]
    public async Task should_classify_final_failure_in_the_original_polly_context()
    {
        var observedKey = new ResiliencePropertyKey<bool>("tests.jobs.retry-observed");
        var finalRetryable = false;
        var options = new JobsRetryOptions
        {
            RetryStrategy = new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                ShouldHandle = args =>
                    ValueTask.FromResult(
                        args.AttemptNumber == 0 || args.Context.Properties.GetValue(observedKey, false)
                    ),
                DelayGenerator = static _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero),
                OnRetry = args =>
                {
                    args.Context.Properties.Set(observedKey, true);
                    return ValueTask.CompletedTask;
                },
            },
        };
        var pipeline = new JobsRetryPipeline(options, TimeProvider.System, NullLogger.Instance);
        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "ContextAware",
            Type = JobType.TimeJob,
            Retries = 1,
        };
        var action = async () =>
            await pipeline.ExecuteAsync(
                job,
                static (_, _) => ValueTask.FromException(new TimeoutException()),
                static (_, _, _) => ValueTask.CompletedTask,
                retryable => finalRetryable = retryable,
                AbortToken
            );

        await action.Should().ThrowAsync<TimeoutException>();
        finalRetryable.Should().BeTrue();
    }

    private static Task<IReadOnlyList<TimeSpan>> _CaptureDelaysAsync(DelayBackoffType backoffType, bool useJitter)
    {
        var options = new JobsRetryOptions
        {
            RetryStrategy = new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = backoffType,
                UseJitter = useJitter,
                Randomizer = static () => 1d,
                MaxDelay = TimeSpan.FromSeconds(5),
                ShouldHandle = static _ => ValueTask.FromResult(true),
            },
        };

        return _ExecuteToExhaustionAsync(options, retries: 2);
    }

    private static async Task<IReadOnlyList<TimeSpan>> _ExecuteToExhaustionAsync(JobsRetryOptions options, int retries)
    {
        var retryDelays = Channel.CreateUnbounded<TimeSpan>();
        var configuredObserver = options.RetryStrategy.OnRetry;
        options.RetryStrategy.OnRetry = async args =>
        {
            if (configuredObserver is not null)
            {
                await configuredObserver(args).ConfigureAwait(false);
            }

            retryDelays.Writer.TryWrite(args.RetryDelay);
        };

        var timeProvider = new FakeTimeProvider();
        var pipeline = new JobsRetryPipeline(options, timeProvider, NullLogger.Instance);
        var job = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "RetryDelay",
            Type = JobType.TimeJob,
            Retries = retries,
            RetryCount = 0,
            RetryIntervals = null,
        };

        var action = async () =>
            await pipeline.ExecuteAsync(
                job,
                static (_, _) => ValueTask.FromException(new TimeoutException()),
                static (_, _, _) => ValueTask.CompletedTask,
                static _ => { },
                AbortToken
            );

        var execution = action.Should().ThrowAsync<TimeoutException>();
        var capturedDelays = new List<TimeSpan>(retries);
        for (var i = 0; i < retries; i++)
        {
            capturedDelays.Add(await retryDelays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromDays(3));
        }

        await execution;
        return capturedDelays;
    }
}
