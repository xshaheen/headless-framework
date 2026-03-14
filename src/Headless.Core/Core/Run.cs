// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Headless.Core;

public static class Run
{
    public static Task DelayedAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> action,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(delay);
        Argument.IsNotNull(action);

        timeProvider ??= TimeProvider.System;

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            async () =>
            {
                if (delay.Ticks > 0)
                {
                    await timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                await action(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken
        );
    }

    #region Retry with Return

    public static async Task<TResult> WithRetriesAsync<TResult, TState>(
        TState state,
        Func<TState, CancellationToken, Task<TResult>> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(action);

        timeProvider ??= TimeProvider.System;
        var attempts = 1;
        var startTime = timeProvider.GetUtcNow();
        var currentBackoffTime = _DefaultBackoffIntervals[0];

        if (retryInterval != null)
        {
            currentBackoffTime = (int)retryInterval.Value.TotalMilliseconds;
        }

        do
        {
            if (attempts > 1 && logger?.IsEnabled(LogLevel.Information) == true)
            {
                logger.LogInformation(
                    "Retrying {Attempts} attempt after {Delay:g}...",
                    attempts,
                    timeProvider.GetUtcNow().Subtract(startTime)
                );
            }

            try
            {
                return await action(state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempts < maxAttempts)
            {
                if (logger?.IsEnabled(LogLevel.Error) == true)
                {
                    logger.LogError(ex, "Retry error: {Message}", ex.Message);
                }

                await timeProvider
                    .DelayUntilElapsedOrCancel(currentBackoffTime.Milliseconds(), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (retryInterval == null)
            {
                currentBackoffTime = _DefaultBackoffIntervals[Math.Min(attempts, _DefaultBackoffIntervals.Length - 1)];
            }

            attempts++;
        } while (attempts <= maxAttempts && !cancellationToken.IsCancellationRequested);

        throw new TaskCanceledException("Should not get here");
    }

    public static Task<TResult> WithRetriesAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            action,
            static (action, token) => action(token),
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    public static Task<TResult> WithRetriesAsync<TResult, TState>(
        TState state,
        Func<TState, Task<TResult>> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            (action, state),
            static (tuple, _) => tuple.action(tuple.state),
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    public static Task<TResult> WithRetriesAsync<TResult>(
        Func<Task<TResult>> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            action,
            static (action, _) => action(),
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    #endregion

    #region Retry without Return

    public static Task WithRetriesAsync<TState>(
        TState state,
        Func<TState, CancellationToken, Task> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            (action, state),
            static async (tuple, token) =>
            {
                await tuple.action(tuple.state, token).ConfigureAwait(false);

                return (object?)null;
            },
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    public static Task WithRetriesAsync(
        Func<CancellationToken, Task> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            action,
            static async (action, token) =>
            {
                await action(token).ConfigureAwait(false);

                return (object?)null;
            },
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    public static Task WithRetriesAsync<TState>(
        TState state,
        Func<TState, Task> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            (action, state),
            static async (tuple, _) =>
            {
                await tuple.action(tuple.state).ConfigureAwait(false);

                return (object?)null;
            },
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    public static Task WithRetriesAsync(
        Func<Task> action,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        return WithRetriesAsync(
            action,
            static async (action, _) =>
            {
                await action().ConfigureAwait(false);

                return (object?)null;
            },
            maxAttempts,
            retryInterval,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    #endregion

    private static readonly int[] _DefaultBackoffIntervals = [100, 1000, 2000, 2000, 5000, 5000, 10000, 30000, 60000];
}
