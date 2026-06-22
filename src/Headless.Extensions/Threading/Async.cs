// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Nito.AsyncEx;

namespace Headless.Threading;

/// <summary>Helpers for running asynchronous code with deterministic disposal and synchronous bridging.</summary>
[PublicAPI]
public static class Async
{
    /// <summary>
    /// Executes an asynchronous method with a provided resource and ensures the resource is disposed of regardless
    /// of the method's outcome. Captures and rethrows any exceptions thrown during the process.
    /// </summary>
    /// <typeparam name="TResource">The type of the disposable resource.</typeparam>
    /// <typeparam name="TReturn">The type of the value produced by <paramref name="body"/>.</typeparam>
    /// <param name="resource">The resource to be used and disposed of after execution.</param>
    /// <param name="body">The method to execute with the given resource as input.</param>
    /// <returns>A task representing the asynchronous operation, with an optional result of type <typeparamref name="TReturn"/> if applicable.</returns>
    /// <remarks>
    /// If both <paramref name="body"/> and <see cref="IAsyncDisposable.DisposeAsync"/> throw, both failures are
    /// surfaced together as an <see cref="AggregateException"/>; otherwise the single failure is rethrown with its
    /// original stack trace preserved.
    /// </remarks>
    public static async Task<TReturn?> Using<TResource, TReturn>(
        TResource resource,
        Func<TResource, Task<TReturn>> body
    )
        where TResource : IAsyncDisposable
    {
        Exception? exception = null;

        var result = default(TReturn);

        try
        {
            result = await body(resource).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception disposeEx)
        {
            // Never let a DisposeAsync failure swallow the original body exception.
            exception = exception is null ? disposeEx : new AggregateException(exception, disposeEx);
        }

        if (exception is not null)
        {
            ExceptionDispatchInfo.Throw(exception);
        }

        return result;
    }

    /// <inheritdoc cref="Using{TResource,TReturn}(TResource,Func{TResource, Task{TReturn}})"/>
    public static Task Using<TResource>(TResource resource, Func<Task> body)
        where TResource : IAsyncDisposable
    {
        return Using(resource, _ => body());
    }

    /// <inheritdoc cref="Using{TResource,TReturn}(TResource,Func{TResource, Task{TReturn}})"/>
    public static Task Using<TResource>(TResource resource, Action body)
        where TResource : IAsyncDisposable
    {
        return Using(
            resource,
            _ =>
            {
                body();

                return Task.CompletedTask;
            }
        );
    }

    /// <inheritdoc cref="Using{TResource,TReturn}(TResource,Func{TResource, Task{TReturn}})"/>
    public static Task Using<TResource>(TResource resource, Func<TResource, Task> body)
        where TResource : IAsyncDisposable
    {
        return Using(
            resource,
            async _ =>
            {
                await body(resource).ConfigureAwait(false);

                return 0;
            }
        );
    }

    /// <inheritdoc cref="Using{TResource,TReturn}(TResource,Func{TResource, Task{TReturn}})"/>
    public static Task<TReturn?> Using<TResource, TReturn>(TResource resource, Func<Task<TReturn>> body)
        where TResource : IAsyncDisposable
    {
        return Using(resource, _ => body());
    }

    /// <summary>Runs an async method synchronously by pumping it on a dedicated single-threaded context.</summary>
    /// <param name="action">An async action.</param>
    /// <remarks>
    /// Any exception thrown by <paramref name="action"/> is rethrown to the caller after the action completes.
    /// </remarks>
    public static void RunSync(this Func<Task> action)
    {
        AsyncContext.Run(action);
    }

    /// <summary>Runs an async method synchronously by pumping it on a dedicated single-threaded context.</summary>
    /// <param name="func">A function that returns a result.</param>
    /// <typeparam name="TResult">Result type.</typeparam>
    /// <returns>Result of the async operation.</returns>
    /// <remarks>
    /// Any exception thrown by <paramref name="func"/> is rethrown to the caller after the function completes.
    /// </remarks>
    public static TResult RunSync<TResult>(this Func<Task<TResult>> func)
    {
        return AsyncContext.Run(func);
    }
}
