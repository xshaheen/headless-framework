// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

namespace Tests.Retry;

public sealed class PollyRetryConformanceTests : TestBase
{
    [Theory]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(OperationCanceledException), false)]
    public async Task should_route_failures_through_explicit_Polly_classification(
        Type exceptionType,
        bool expectedRetry
    )
    {
        var retried = false;
        var stopped = false;
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        var pipeline = _Pipeline(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is TimeoutException),
            }
        );

        await pipeline.ExecuteAsync(
            (_, _) => Task.FromResult(MessagingRetryAttempt.Retryable(OperateResult.Failed(exception))),
            (_, _, _, _, _) =>
            {
                retried = true;
                return Task.FromResult(false);
            },
            (_, _, _) =>
            {
                stopped = true;
                return Task.CompletedTask;
            },
            Guid.Empty,
            AbortToken
        );

        retried.Should().Be(expectedRetry);
        stopped.Should().Be(!expectedRetry);
    }

    [Fact]
    public async Task should_use_Polly_exponential_delay_and_attempt_numbering()
    {
        var delays = new List<TimeSpan>();
        var attempts = 0;
        var pipeline = _Pipeline(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(10),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = static _ => ValueTask.FromResult(true),
            }
        );

        await pipeline.ExecuteAsync(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(MessagingRetryAttempt.Retryable(OperateResult.Failed(new TimeoutException())));
            },
            (_, _, delay, _, _) =>
            {
                delays.Add(delay);
                return Task.FromResult(delays.Count == 1);
            },
            static (_, _, _) => Task.CompletedTask,
            Guid.Empty,
            AbortToken
        );

        attempts.Should().Be(2);
        delays.Should().Equal(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task should_persist_a_later_permanent_failure_after_a_transient_retry()
    {
        var attempts = 0;
        Exception? stoppedException = null;
        var pipeline = _Pipeline(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is TimeoutException),
            }
        );

        await pipeline.ExecuteAsync(
            (_, _) =>
            {
                Exception exception =
                    attempts++ == 0 ? new TimeoutException("transient") : new ArgumentException("permanent");
                return Task.FromResult(MessagingRetryAttempt.Retryable(OperateResult.Failed(exception)));
            },
            static (_, _, _, _, _) => Task.FromResult(true),
            (_, exception, _) =>
            {
                stoppedException = exception;
                return Task.CompletedTask;
            },
            Guid.Empty,
            AbortToken
        );

        attempts.Should().Be(2);
        stoppedException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task should_not_notify_Polly_observer_when_durable_budget_stops_retry()
    {
        var observerCalls = 0;
        var pipeline = _Pipeline(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = static _ => ValueTask.FromResult(true),
                OnRetry = _ =>
                {
                    observerCalls++;
                    return ValueTask.CompletedTask;
                },
            }
        );

        await pipeline.ExecuteAsync(
            static (_, _) =>
                Task.FromResult(MessagingRetryAttempt.Retryable(OperateResult.Failed(new TimeoutException()))),
            static (_, _, _, _, _) => Task.FromResult(false),
            static (_, _, _) => Task.CompletedTask,
            Guid.Empty,
            AbortToken
        );

        observerCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_force_exhaustion_when_Polly_predicate_throws()
    {
        await _ShouldForceExhaustionAsync(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = static _ => ValueTask.FromException<bool>(new InvalidOperationException("predicate")),
            }
        );
    }

    [Fact]
    public async Task should_force_exhaustion_when_Polly_delay_generator_throws()
    {
        await _ShouldForceExhaustionAsync(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                ShouldHandle = static _ => ValueTask.FromResult(true),
                DelayGenerator = static _ => ValueTask.FromException<TimeSpan?>(new InvalidOperationException("delay")),
            }
        );
    }

    [Fact]
    public async Task should_honor_jitter_and_cap_custom_delays()
    {
        var jittered = await _CaptureDelayAsync(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Randomizer = static () => 1d,
                MaxDelay = TimeSpan.FromSeconds(5),
                ShouldHandle = static _ => ValueTask.FromResult(true),
            }
        );
        var custom = await _CaptureDelayAsync(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                MaxDelay = TimeSpan.FromHours(1),
                ShouldHandle = static _ => ValueTask.FromResult(true),
                DelayGenerator = static _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.FromHours(48)),
            }
        );

        jittered.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
        custom.Should().Be(TimeSpan.FromHours(1));
    }

    private static async Task<TimeSpan> _CaptureDelayAsync(RetryStrategyOptions strategy)
    {
        var delay = TimeSpan.Zero;
        await _Pipeline(strategy)
            .ExecuteAsync(
                static (_, _) =>
                    Task.FromResult(MessagingRetryAttempt.Retryable(OperateResult.Failed(new TimeoutException()))),
                (_, _, retryDelay, _, _) =>
                {
                    delay = retryDelay;
                    return Task.FromResult(false);
                },
                static (_, _, _) => Task.CompletedTask,
                Guid.Empty,
                AbortToken
            );
        return delay;
    }

    private static async Task _ShouldForceExhaustionAsync(RetryStrategyOptions strategy)
    {
        var strategyFailed = false;
        await _Pipeline(strategy)
            .ExecuteAsync(
                static (_, _) =>
                    Task.FromResult(MessagingRetryAttempt.Retryable(OperateResult.Failed(new TimeoutException()))),
                (_, _, _, failed, _) =>
                {
                    strategyFailed = failed;
                    return Task.FromResult(false);
                },
                static (_, _, _) => Task.CompletedTask,
                Guid.NewGuid(),
                AbortToken
            );

        strategyFailed.Should().BeTrue();
    }

    private static MessagingRetryPipeline _Pipeline(RetryStrategyOptions strategy) =>
        new(new RetryPolicyOptions { RetryStrategy = strategy }, TimeProvider.System, NullLogger.Instance);
}
