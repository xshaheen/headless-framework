// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Asynchronous variants of the <see cref="ApiResult{T}"/> <c>Map</c>, <c>Bind</c>, and <c>Match</c> operations,
/// including overloads that operate on a <see cref="Task{TResult}"/> of <see cref="ApiResult{T}"/>.
/// </summary>
[PublicAPI]
#pragma warning disable VSTHRD003 // These extension methods intentionally await externally-provided tasks with ConfigureAwait(false).
public static class OpResultAsyncExtensions
{
    extension<T>(ApiResult<T> result)
    {
        /// <summary>Asynchronously transforms the success value, propagating the error unchanged on failure.</summary>
        /// <typeparam name="TOut">The mapped value type.</typeparam>
        /// <param name="mapper">The asynchronous projection applied to the success value.</param>
        /// <returns>A task producing a successful result with the mapped value, or the original failure.</returns>
        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> mapper)
        {
            return result.IsSuccess
                ? await mapper(result.Value).ConfigureAwait(false)
                : ApiResult<TOut>.Fail(result.Error);
        }

        /// <summary>Asynchronously chains another result-producing operation on success, propagating the error unchanged on failure.</summary>
        /// <typeparam name="TOut">The value type produced by <paramref name="binder"/>.</typeparam>
        /// <param name="binder">The asynchronous function applied to the success value, producing the next result.</param>
        /// <returns>A task producing the result of <paramref name="binder"/>, or the original failure.</returns>
        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, Task<ApiResult<TOut>>> binder)
        {
            return result.IsSuccess
                ? await binder(result.Value).ConfigureAwait(false)
                : ApiResult<TOut>.Fail(result.Error);
        }
    }

    extension<T>(Task<ApiResult<T>> resultTask)
    {
        /// <summary>Awaits the result, then invokes <paramref name="success"/> or <paramref name="failure"/> and returns its result.</summary>
        /// <typeparam name="TResult">The type produced by both branches.</typeparam>
        /// <param name="success">The function invoked on success, receiving the value.</param>
        /// <param name="failure">The function invoked on failure, receiving the error.</param>
        /// <returns>A task producing the value of the invoked branch.</returns>
        public async Task<TResult> MatchAsync<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Match(success, failure);
        }

        /// <summary>Awaits the result, then synchronously transforms the success value, propagating the error on failure.</summary>
        /// <typeparam name="TOut">The mapped value type.</typeparam>
        /// <param name="mapper">The projection applied to the success value.</param>
        /// <returns>A task producing a successful result with the mapped value, or the original failure.</returns>
        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, TOut> mapper)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Map(mapper);
        }

        /// <summary>Awaits the result, then asynchronously transforms the success value, propagating the error on failure.</summary>
        /// <typeparam name="TOut">The mapped value type.</typeparam>
        /// <param name="mapper">The asynchronous projection applied to the success value.</param>
        /// <returns>A task producing a successful result with the mapped value, or the original failure.</returns>
        public async Task<ApiResult<TOut>> MapAsync<TOut>(Func<T, Task<TOut>> mapper)
        {
            var result = await resultTask.ConfigureAwait(false);
            return await result.MapAsync(mapper).ConfigureAwait(false);
        }

        /// <summary>Awaits the result, then synchronously chains another result-producing operation on success.</summary>
        /// <typeparam name="TOut">The value type produced by <paramref name="binder"/>.</typeparam>
        /// <param name="binder">The function applied to the success value, producing the next result.</param>
        /// <returns>A task producing the result of <paramref name="binder"/>, or the original failure.</returns>
        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, ApiResult<TOut>> binder)
        {
            var result = await resultTask.ConfigureAwait(false);
            return result.Bind(binder);
        }

        /// <summary>Awaits the result, then asynchronously chains another result-producing operation on success.</summary>
        /// <typeparam name="TOut">The value type produced by <paramref name="binder"/>.</typeparam>
        /// <param name="binder">The asynchronous function applied to the success value, producing the next result.</param>
        /// <returns>A task producing the result of <paramref name="binder"/>, or the original failure.</returns>
        public async Task<ApiResult<TOut>> BindAsync<TOut>(Func<T, Task<ApiResult<TOut>>> binder)
        {
            var result = await resultTask.ConfigureAwait(false);
            return await result.BindAsync(binder).ConfigureAwait(false);
        }
    }
}
