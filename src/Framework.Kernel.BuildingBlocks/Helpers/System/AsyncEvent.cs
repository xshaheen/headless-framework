using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

/// <summary>Represents an asynchronous event that can have multiple handlers.</summary>
/// <typeparam name="TEventArgs">The type of event arguments.</typeparam>
/// <param name="parallelInvoke">Indicates whether to invoke handlers in parallel.</param>
/// <remarks>
/// This class differs from a delegate event in that it allows for asynchronous invocation of event handlers,
/// and provides the option to invoke handlers in parallel. It also implements the IObservable interface,
/// allowing observers to subscribe to the event.
/// </remarks>
public sealed class AsyncEvent<TEventArgs>(bool parallelInvoke = false) : IObservable<TEventArgs>, IDisposable
    where TEventArgs : EventArgs
{
    private readonly List<Func<object, TEventArgs, Task>> _invocationList = [];
    private readonly object _lockObject = new();

    // ReSharper disable once InconsistentlySynchronizedField
    public bool HasHandlers => _invocationList.Count > 0;

    /// <summary>Adds an asynchronous event handler to the invocation list.</summary>
    /// <param name="callback">The event handler to add.</param>
    /// <returns>An IDisposable that can be used to remove the event handler.</returns>
    public IDisposable AddHandler(Func<object, TEventArgs, Task> callback)
    {
        Argument.IsNotNull(callback);

        lock (_lockObject)
        {
            _invocationList.Add(callback);
        }

        return new EventHandlerDisposable<TEventArgs>(this, callback);
    }

    /// <summary>Adds a synchronous event handler to the invocation list.</summary>
    /// <param name="callback">The event handler to add.</param>
    /// <returns>An IDisposable that can be used to remove the event handler.</returns>
    public IDisposable AddHandler(Action<object, TEventArgs> callback)
    {
        return AddHandler(
            (sender, args) =>
            {
                callback(sender, args);
                return Task.CompletedTask;
            }
        );
    }

    /// <summary>Removes an asynchronous event handler from the invocation list.</summary>
    /// <param name="callback">The event handler to remove.</param>
    public void RemoveHandler(Func<object, TEventArgs, Task> callback)
    {
        Argument.IsNotNull(callback);

        lock (_lockObject)
        {
            _invocationList.Remove(callback);
        }
    }

    /// <summary>Invokes all event handlers asynchronously.</summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="eventArgs">The event data.</param>
    public async Task InvokeAsync(object sender, TEventArgs eventArgs)
    {
        List<Func<object, TEventArgs, Task>> tmpInvocationList;

        lock (_lockObject)
        {
            tmpInvocationList = [.. _invocationList];
        }

        if (parallelInvoke)
        {
            await Task.WhenAll(tmpInvocationList.Select(callback => callback(sender, eventArgs)))
                .WithAggregatedExceptions()
                .AnyContext();
        }
        else
        {
            foreach (var callback in tmpInvocationList)
            {
                await callback(sender, eventArgs).AnyContext();
            }
        }
    }

    /// <summary>Subscribes an observer to the event.</summary>
    /// <param name="observer">The observer to subscribe.</param>
    /// <returns>An IDisposable that can be used to unsubscribe the observer.</returns>
    public IDisposable Subscribe(IObserver<TEventArgs> observer)
    {
        return AddHandler((_, args) => observer.OnNext(args));
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            _invocationList.Clear();
        }
    }

    /// <summary>Represents a disposable event handler.</summary>
    /// <typeparam name="T">The type of event arguments.</typeparam>
    private sealed class EventHandlerDisposable<T>(AsyncEvent<T> @event, Func<object, T, Task> callback) : IDisposable
        where T : EventArgs
    {
        public void Dispose()
        {
            @event.RemoveHandler(callback);
        }
    }
}
