// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;

namespace Framework.BuildingBlocks.Helpers.System;
[PublicAPI]
public static class Async
{
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
            var info = ExceptionDispatchInfo.Capture(exception);
            info.Throw();
        }

        return result;
    }

    public static Task Using<TResource>(TResource resource, Func<Task> body)
        where TResource : IAsyncDisposable
    {
        return Using(resource, _ => body());
    }

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

    public static Task Using<TResource>(TResource resource, Func<TResource, Task> body)
        where TResource : IAsyncDisposable
    {
        return Using(
            resource,
            async _ =>
            {
                await body(resource).AnyContext();

                return Task.CompletedTask;
            }
        );
    }

    public static Task<TReturn?> Using<TResource, TReturn>(TResource resource, Func<Task<TReturn>> body)
        where TResource : IAsyncDisposable
    {
        return Using(resource, _ => body());
    }
}
