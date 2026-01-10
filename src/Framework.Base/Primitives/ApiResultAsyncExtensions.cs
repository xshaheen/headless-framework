// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

/// <summary>
/// Async variants for OpResult operations.
/// </summary>
[PublicAPI]
public static class OpResultAsyncExtensions
{
    extension<T>(ApiResult<T> result)
    {
        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> mapper)
        {
            return result.IsSuccess ? await mapper(result.Value).AnyContext() : ApiResult<TOut>.Fail(result.Error);
        }

        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, Task<ApiResult<TOut>>> binder)
        {
            return result.IsSuccess ? await binder(result.Value).AnyContext() : ApiResult<TOut>.Fail(result.Error);
        }
    }

    extension<T>(Task<ApiResult<T>> resultTask)
    {
        public async Task<TResult> MatchAsync<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure)
        {
            var result = await resultTask.AnyContext();
            return result.Match(success, failure);
        }

        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, TOut> mapper)
        {
            var result = await resultTask.AnyContext();
            return result.Map(mapper);
        }

        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> mapper)
        {
            var result = await resultTask.AnyContext();
            return await result.MapAsync(mapper).AnyContext();
        }

        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, ApiResult<TOut>> binder)
        {
            var result = await resultTask.AnyContext();
            return result.Bind(binder);
        }

        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, Task<ApiResult<TOut>>> binder)
        {
            var result = await resultTask.AnyContext();
            return await result.BindAsync(binder).AnyContext();
        }
    }
}
