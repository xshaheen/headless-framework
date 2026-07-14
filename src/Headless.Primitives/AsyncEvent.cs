// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>Represents an asynchronous event that can have multiple handlers.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <remarks>
/// This class differs from a delegate event in which it allows for asynchronous invocation of event handlers,
/// and provides the option to invoke handlers in parallel.
/// It also implements the IObservable interface, allowing observers to subscribe to the event.
/// </remarks>
[PublicAPI]
public interface IAsyncEvent<TEvent> : IObservable<TEvent>
    where TEvent : EventArgs
{
    /// <summary>Indicates whether to invoke handlers in parallel.</summary>
    bool ParallelInvoke { get; }

    /// <summary>Indicates whether the event has any handlers.</summary>
    bool HasHandlers { get; }

    /// <summary>Adds an asynchronous event handler to the invocation list.</summary>
    /// <param name="callback">The event handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes the registered handler when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(Func<object, TEvent, Task> callback);

    /// <summary>Adds a synchronous event handler to the invocation list.</summary>
    /// <param name="callback">The event handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes the registered handler when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(Action<object, TEvent> callback);

    /// <summary>Removes an asynchronous event handler from the invocation list.</summary>
    /// <param name="callback">The event handler to remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    void RemoveHandler(Func<object, TEvent, Task> callback);

    /// <summary>Invokes all registered event handlers asynchronously over a snapshot of the invocation list.</summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="eventArgs">The event data.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the handlers to complete.</param>
    /// <returns>A <see cref="Task"/> that completes once every handler has run.</returns>
    /// <remarks>
    /// When <see cref="ParallelInvoke"/> is <see langword="true"/>, handlers run concurrently and any failures are
    /// surfaced together; when <see langword="false"/>, handlers run sequentially and the first handler exception
    /// stops the remaining handlers and propagates to the caller. Cancellation is observed before dispatch and,
    /// in the sequential case, between handlers; individual handler delegates do not receive the token.
    /// </remarks>
    Task InvokeAsync(object sender, TEvent eventArgs, CancellationToken cancellationToken = default);

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
[PublicAPI]
public sealed class AsyncEvent<TEvent>(bool parallelInvoke = false) : IAsyncEvent<TEvent>
    where TEvent : EventArgs
{
    private readonly List<Func<object, TEvent, Task>> _eventHandlers = [];
    private readonly Lock _lockObject = new();

    /// <inheritdoc />
    public bool ParallelInvoke { get; } = parallelInvoke;

    // ReSharper disable once InconsistentlySynchronizedField
    /// <inheritdoc />
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
    public async Task InvokeAsync(object sender, TEvent eventArgs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<Func<object, TEvent, Task>> tmpInvocationList;

        lock (_lockObject)
        {
            tmpInvocationList = [.. _eventHandlers];
        }

        if (ParallelInvoke)
        {
            // Materialize the task list eagerly, turning a synchronous throw (before a handler's first await) into a
            // faulted task. Passing the lazy Select to Task.WhenAll would stop enumeration at the first synchronous
            // throw, leaving earlier handlers orphaned and later handlers never started.
            var tasks = new List<Task>(tmpInvocationList.Count);

            foreach (var callback in tmpInvocationList)
            {
                try
                {
                    tasks.Add(callback(sender, eventArgs));
                }
                catch (Exception e)
                {
                    tasks.Add(Task.FromException(e));
                }
            }

            await Task.WhenAll(tasks).WithAggregatedExceptions().ConfigureAwait(false);
        }
        else
        {
            foreach (var callback in tmpInvocationList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await callback(sender, eventArgs).ConfigureAwait(false);
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
