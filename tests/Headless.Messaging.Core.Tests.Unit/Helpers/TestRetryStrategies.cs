// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Retry;
using Polly;
using Polly.Retry;

namespace Tests.Helpers;

internal static class TestRetryStrategies
{
    public static RetryStrategyOptions ZeroDelay(int maxRetryAttempts) => FixedDelay(maxRetryAttempts, TimeSpan.Zero);

    public static RetryStrategyOptions FixedDelay(int maxRetryAttempts, TimeSpan delay) =>
        new()
        {
            MaxRetryAttempts = maxRetryAttempts,
            Delay = delay,
            BackoffType = DelayBackoffType.Constant,
            ShouldHandle = static args =>
                ValueTask.FromResult(
                    args.Outcome.Exception is { } exception
                        && exception is not OperationCanceledException
                        && !RetryExceptionClassifier.IsPermanent(exception)
                ),
        };

    public static RetryStrategyOptions PermanentArgument(int maxRetryAttempts) =>
        new()
        {
            MaxRetryAttempts = maxRetryAttempts,
            Delay = TimeSpan.Zero,
            ShouldHandle = static args =>
                ValueTask.FromResult(
                    args.Outcome.Exception is { } exception && !RetryExceptionClassifier.IsPermanent(exception)
                ),
        };
}
