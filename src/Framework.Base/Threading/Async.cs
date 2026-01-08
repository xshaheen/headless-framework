// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Nito.AsyncEx;

namespace Framework.Threading;

[PublicAPI]
public static class Async
{
    /// <summary>
    /// Executes an asynchronous method with a provided resource and ensures the resource is disposed of regardless
    /// of the method's outcome. Captures and rethrows any exceptions thrown during the process.
    /// </summary>
    /// <param name="resource">The resource to be used and disposed of after execution.</param>
    /// <param name="body">The method to execute with the given resource as input.</param>
    /// <returns>A task representing the asynchronous operation, with an optional result of type <typeparamref name="TReturn"/> if applicable.</returns>
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
            result = await body(resource).AnyContext();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        await resource.DisposeAsync();

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
                await body(resource).AnyContext();

                return 0;
            }
        );
    }

    public static Task<TReturn?> Using<TResource, TReturn>(TResource resource, Func<Task<TReturn>> body)
        where TResource : IAsyncDisposable
    {
        return Using(resource, _ => body());
    }

    /// <summary>Runs an async method synchronously.</summary>
    /// <param name="action">An async action</param>
    public static void RunSync(this Func<Task> action)
    {
        AsyncContext.Run(action);
    }

    /// <summary>Runs an async method synchronously.</summary>
    /// <param name="func">A function that returns a result</param>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <returns>Result of the async operation</returns>
    public static TResult RunSync<TResult>(this Func<Task<TResult>> func)
    {
        return AsyncContext.Run(func);
    }
}
