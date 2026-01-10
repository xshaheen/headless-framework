// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Async variants for OpResult operations.
/// </summary>
[PublicAPI]
public static class OpResultAsyncExtensions
{
    public static async Task<OpResult<TOut>> MapAsync<T, TOut>(this OpResult<T> result, Func<T, Task<TOut>> mapper) =>
        result.IsSuccess ? await mapper(result.Value).AnyContext() : OpResult<TOut>.Fail(result.Error);

    public static async Task<OpResult<TOut>> BindAsync<T, TOut>(
        this OpResult<T> result,
        Func<T, Task<OpResult<TOut>>> binder
    ) => result.IsSuccess ? await binder(result.Value).AnyContext() : OpResult<TOut>.Fail(result.Error);

    public static async Task<TResult> MatchAsync<T, TResult>(
        this Task<OpResult<T>> resultTask,
        Func<T, TResult> success,
        Func<ResultError, TResult> failure
    )
    {
        var result = await resultTask.AnyContext();
        return result.Match(success, failure);
    }

    public static async Task<OpResult<TOut>> MapAsync<T, TOut>(this Task<OpResult<T>> resultTask, Func<T, TOut> mapper)
    {
        var result = await resultTask.AnyContext();
        return result.Map(mapper);
    }

    public static async Task<OpResult<TOut>> MapAsync<T, TOut>(
        this Task<OpResult<T>> resultTask,
        Func<T, Task<TOut>> mapper
    )
    {
        var result = await resultTask.AnyContext();
        return await result.MapAsync(mapper).AnyContext();
    }

    public static async Task<OpResult<TOut>> BindAsync<T, TOut>(
        this Task<OpResult<T>> resultTask,
        Func<T, OpResult<TOut>> binder
    )
    {
        var result = await resultTask.AnyContext();
        return result.Bind(binder);
    }

    public static async Task<OpResult<TOut>> BindAsync<T, TOut>(
        this Task<OpResult<T>> resultTask,
        Func<T, Task<OpResult<TOut>>> binder
    )
    {
        var result = await resultTask.AnyContext();
        return await result.BindAsync(binder).AnyContext();
    }
}
