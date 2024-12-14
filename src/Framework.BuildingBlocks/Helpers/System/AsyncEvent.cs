// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.BuildingBlocks.Helpers.System;

/// <summary>Represents an asynchronous event that can have multiple handlers.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <remarks>
/// This class differs from a delegate event in which it allows for asynchronous invocation of event handlers,
/// and provides the option to invoke handlers in parallel.
/// It also implements the IObservable interface, allowing observers to subscribe to the event.
/// </remarks>
public interface IAsyncEvent<TEvent> : IObservable<TEvent>
    where TEvent : EventArgs
{
    /// <summary>>Indicates whether to invoke handlers in parallel.</summary>
    bool ParallelInvoke { get; }

    /// <summary>Indicates whether the event has any handlers.</summary>
    bool HasHandlers { get; }

    /// <summary>Adds an asynchronous event handler to the invocation list.</summary>
    /// <param name="callback">The event handler to add.</param>
    /// <returns>An IDisposable that can be used to remove the event handler.</returns>
    IDisposable AddHandler(Func<object, TEvent, Task> callback);

    /// <summary>Adds a synchronous event handler to the invocation list.</summary>
    /// <param name="callback">The event handler to add.</param>
    /// <returns>An IDisposable that can be used to remove the event handler.</returns>
    IDisposable AddHandler(Action<object, TEvent> callback);

    /// <summary>Removes an asynchronous event handler from the invocation list.</summary>
    /// <param name="callback">The event handler to remove.</param>
    void RemoveHandler(Func<object, TEvent, Task> callback);

    /// <summary>Invokes all event handlers asynchronously.</summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="eventArgs">The event data.</param>
    Task InvokeAsync(object sender, TEvent eventArgs);

    /// <summary>Clear the event handlers.</summary>
    void ClearHandlers();
}

/// <summary>Represents an asynchronous event that can have multiple handlers.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <param name="parallelInvoke">Indicates whether to invoke handlers in parallel.</param>
/// <remarks>
/// This class differs from a delegate event in that it allows for asynchronous invocation of event handlers,
/// and provides the option to invoke handlers in parallel. It also implements the IObservable interface,
/// allowing observers to subscribe to the event.
/// </remarks>
public sealed class AsyncEvent<TEvent>(bool parallelInvoke = false) : IAsyncEvent<TEvent>
    where TEvent : EventArgs
{
    private readonly List<Func<object, TEvent, Task>> _eventHandlers = [];
    private readonly Lock _lockObject = new();

    public bool ParallelInvoke { get; } = parallelInvoke;

    // ReSharper disable once InconsistentlySynchronizedField
    public bool HasHandlers => _eventHandlers.Count > 0;

    /// <inheritdoc />
    public IDisposable AddHandler(Func<object, TEvent, Task> callback)
    {
        Argument.IsNotNull(callback);

        lock (_lockObject)
        {
            _eventHandlers.Add(callback);
        }

        return new EventHandlerDisposable<TEvent>(this, callback);
    }

    /// <inheritdoc />
    public IDisposable AddHandler(Action<object, TEvent> callback)
    {
        return AddHandler(
            (sender, args) =>
            {
                callback(sender, args);
                return Task.CompletedTask;
            }
        );
    }

    /// <inheritdoc />
    public void RemoveHandler(Func<object, TEvent, Task> callback)
    {
        Argument.IsNotNull(callback);

        lock (_lockObject)
        {
            _eventHandlers.Remove(callback);
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync(object sender, TEvent eventArgs)
    {
        List<Func<object, TEvent, Task>> tmpInvocationList;

        lock (_lockObject)
        {
            tmpInvocationList = [.. _eventHandlers];
        }

        if (ParallelInvoke)
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

    /// <inheritdoc />
    public IDisposable Subscribe(IObserver<TEvent> observer)
    {
        return AddHandler((_, args) => observer.OnNext(args));
    }

    /// <inheritdoc />
    public void ClearHandlers()
    {
        lock (_lockObject)
        {
            _eventHandlers.Clear();
        }
    }

    #region Helpers

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

    #endregion
}
