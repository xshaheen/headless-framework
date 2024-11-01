// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Polly;
using Polly.Retry;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

[PublicAPI]
public static class Run
{
    private static readonly Dictionary<
        (int MaxAttempts, TimeSpan? RetryInterval, TimeProvider? TimeProvider),
        ResiliencePipeline
    > _RetryPipelines = [];

    public static Task DelayedAsync(
        TimeSpan delay,
        Func<Task> action,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        timeProvider ??= TimeProvider.System;

        return cancellationToken.IsCancellationRequested
            ? Task.CompletedTask
            : Task.Run(
                async () =>
                {
                    if (delay.Ticks > 0)
                    {
                        await Task.Delay(delay, timeProvider, cancellationToken).AnyContext();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await action().AnyContext();
                },
                cancellationToken
            );
    }

    public static void WithRetries(
        Action callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        resiliencePipeline.Execute(callback);
    }

    public static TResult WithRetries<TResult>(
        Func<TResult> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return resiliencePipeline.Execute(static callback => callback(), callback);
    }

    public static async Task WithRetriesAsync(
        Func<Task> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        await resiliencePipeline.ExecuteAsync(
            static async (callback, _) => await callback(),
            callback,
            cancellationToken
        );
    }

    public static async Task<TResult> WithRetriesAsync<TResult>(
        Func<Task<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return await resiliencePipeline.ExecuteAsync(
            static async (callback, _) => await callback(),
            callback,
            cancellationToken
        );
    }

    public static async Task<TResult> WithRetriesAsync<TResult>(
        Func<CancellationToken, Task<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return await resiliencePipeline.ExecuteAsync(
            static async (callback, token) => await callback(token),
            callback,
            cancellationToken
        );
    }

    public static ValueTask WithRetriesAsync(
        Func<ValueTask> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return resiliencePipeline.ExecuteAsync(static (callback, _) => callback(), callback, cancellationToken);
    }

    public static ValueTask<TResult> WithRetriesAsync<TResult>(
        Func<ValueTask<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return resiliencePipeline.ExecuteAsync(static (callback, _) => callback(), callback, cancellationToken);
    }

    public static ValueTask<TResult> WithRetriesAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return resiliencePipeline.ExecuteAsync(callback, cancellationToken);
    }

    public static async Task<TResult> WithRetriesAsync<TResult, TState>(
        TState state,
        Func<TState, CancellationToken, Task<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        var newState = (state, callback);

        return await resiliencePipeline.ExecuteAsync(
            static async (newState, token) => await newState.callback(newState.state, token),
            newState,
            cancellationToken
        );
    }

    public static async Task<TResult> WithRetriesAsync<TResult, TState>(
        TState state,
        Func<TState, Task<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        var newState = (state, callback);

        return await resiliencePipeline.ExecuteAsync(
            static async (newState, _) => await newState.callback(newState.state),
            newState,
            cancellationToken
        );
    }

    public static ValueTask<TResult> WithRetriesAsync<TResult, TState>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> callback,
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        var resiliencePipeline = _CreateRetryPipeline(maxAttempts, retryInterval, timeProvider);

        return resiliencePipeline.ExecuteAsync(callback, state, cancellationToken);
    }

    private static ResiliencePipeline _CreateRetryPipeline(
        int maxAttempts = 5,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null
    )
    {
        var key = (maxAttempts, retryInterval, timeProvider);

        if (_RetryPipelines.TryGetValue(key, out var resiliencePipeline))
        {
            return resiliencePipeline;
        }

        var pipeline = _CoreCreateResiliencePipeline(maxAttempts, retryInterval, timeProvider);

        _RetryPipelines[key] = pipeline;

        return pipeline;
    }

    private static ResiliencePipeline _CoreCreateResiliencePipeline(
        int maxAttempts,
        TimeSpan? retryInterval,
        TimeProvider? timeProvider
    )
    {
        var options = new RetryStrategyOptions
        {
            Name = "RunWithRetriesPolicy",
            MaxRetryAttempts = maxAttempts,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            Delay = retryInterval ?? TimeSpan.FromSeconds(1),
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
        };

        var builder = new ResiliencePipelineBuilder { TimeProvider = timeProvider };

        return builder.AddRetry(options).Build();
    }
}
