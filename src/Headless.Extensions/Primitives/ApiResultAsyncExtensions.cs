// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Async variants for OpResult operations.
/// </summary>
[PublicAPI]
#pragma warning disable VSTHRD003 // These extension methods intentionally await externally-provided tasks with ConfigureAwait(false).
public static class OpResultAsyncExtensions
{
    extension<T>(ApiResult<T> result)
    {
        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> mapper)
        {
            return result.IsSuccess
                ? await mapper(result.Value).ConfigureAwait(false)
                : ApiResult<TOut>.Fail(result.Error);
        }

        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, Task<ApiResult<TOut>>> binder)
        {
            return result.IsSuccess
                ? await binder(result.Value).ConfigureAwait(false)
                : ApiResult<TOut>.Fail(result.Error);
        }
    }

    extension<T>(Task<ApiResult<T>> resultTask)
    {
        public async Task<TResult> MatchAsync<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(success, failure);
        }

        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, TOut> mapper)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Map(mapper);
        }

        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> mapper)
        {
            var result = await resultTask.ConfigureAwait(false);
            return await result.MapAsync(mapper).ConfigureAwait(false);
        }

        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, ApiResult<TOut>> binder)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(binder);
        }

        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, Task<ApiResult<TOut>>> binder)
        {
            var result = await resultTask.ConfigureAwait(false);
            return await result.BindAsync(binder).ConfigureAwait(false);
        }
    }
}
