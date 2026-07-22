// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>An asynchronous, cancellable event handler.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <param name="sender">The source of the event.</param>
/// <param name="eventArgs">The event data.</param>
/// <param name="cancellationToken">A token observed while the handler runs.</param>
[PublicAPI]
public delegate ValueTask AsyncEventHandler<in TEvent>(
    object sender,
    TEvent eventArgs,
    CancellationToken cancellationToken
)
    where TEvent : EventArgs;

/// <summary>Represents an asynchronous event that can have multiple handlers.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <remarks>
/// Unlike a delegate <c>event</c>, this invokes its handlers asynchronously (each may return a <see cref="ValueTask"/>),
/// optionally in parallel, and supports both asynchronous and synchronous handlers. Subscriptions are identified by the
/// returned <see cref="IDisposable"/> (registration identity), so the same delegate can be added more than once and each
/// registration removed independently. It also implements <see cref="IObservable{T}"/>.
/// </remarks>
[PublicAPI]
public interface IAsyncEvent<TEvent> : IObservable<TEvent>
    where TEvent : EventArgs
{
    /// <summary>Indicates whether handlers are invoked in parallel (see <see cref="InvokeAsync"/>).</summary>
    bool ParallelInvoke { get; }

    /// <summary>Indicates whether the event currently has any handlers. Thread-safe.</summary>
    bool HasHandlers { get; }

    /// <summary>Adds an asynchronous event handler.</summary>
    /// <param name="callback">The handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes this specific registration when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(AsyncEventHandler<TEvent> callback);

    /// <summary>Adds a synchronous event handler.</summary>
    /// <param name="callback">The handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes this specific registration when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(Action<object, TEvent> callback);

    /// <summary>
    /// Invokes every handler over an allocation-free snapshot of the current registrations and <b>propagates</b> handler
    /// exceptions to the caller.
    /// </summary>
    /// <remarks>
    /// Sequential (default): handlers run in order and the first exception stops the rest and propagates. Parallel
    /// (<see cref="ParallelInvoke"/>): handlers start together and their exceptions are aggregated. Use
    /// <see cref="SafeInvokeAsync"/> when one handler's failure must not affect the others or the caller.
    /// </remarks>
    ValueTask InvokeAsync(object sender, TEvent eventArgs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes <b>every</b> handler sequentially, isolating each handler's exception through <paramref name="onHandlerError"/>
    /// so one failing handler neither stops the others nor propagates to the caller.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="eventArgs">The event data.</param>
    /// <param name="onHandlerError">Invoked with each handler exception; must not throw.</param>
    /// <param name="cancellationToken">A token passed to each handler.</param>
    ValueTask SafeInvokeAsync(
        object sender,
        TEvent eventArgs,
        Action<Exception> onHandlerError,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes all handlers.</summary>
    void ClearHandlers();
}

/// <summary>The default <see cref="IAsyncEvent{TEvent}"/> implementation, backed by a copy-on-write registration array.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <param name="parallelInvoke">Indicates whether <see cref="InvokeAsync"/> invokes handlers in parallel.</param>
[PublicAPI]
public sealed class AsyncEvent<TEvent>(bool parallelInvoke = false) : IAsyncEvent<TEvent>
    where TEvent : EventArgs
{
    // Copy-on-write registration array: subscriptions are rare (mutations take the lock and swap a new array), while
    // invocation is a lock-free, allocation-free read of the current snapshot. `volatile` publishes the swap safely.
    private volatile Subscription[] _handlers = [];
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public bool ParallelInvoke { get; } = parallelInvoke;

    /// <inheritdoc />
    public bool HasHandlers => _handlers.Length > 0;

    /// <inheritdoc />
    public IDisposable AddHandler(AsyncEventHandler<TEvent> callback)
    {
        Argument.IsNotNull(callback);

        var subscription = new Subscription(this, callback);

        lock (_lock)
        {
            var current = _handlers;
            var updated = new Subscription[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[^1] = subscription;
            _handlers = updated;
        }

        return subscription;
    }

    /// <inheritdoc />
    public IDisposable AddHandler(Action<object, TEvent> callback)
    {
        Argument.IsNotNull(callback);

        return AddHandler(
            (sender, args, _) =>
            {
                callback(sender, args);
                return default;
            }
        );
    }

    /// <inheritdoc />
    public ValueTask InvokeAsync(object sender, TEvent eventArgs, CancellationToken cancellationToken = default)
    {
        var handlers = _handlers;

        if (handlers.Length == 0)
        {
            return default;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (handlers.Length == 1)
        {
            return handlers[0].Callback(sender, eventArgs, cancellationToken);
        }

        return ParallelInvoke
            ? _InvokeParallelAsync(handlers, sender, eventArgs, cancellationToken)
            : _InvokeSequentialAsync(handlers, sender, eventArgs, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SafeInvokeAsync(
        object sender,
        TEvent eventArgs,
        Action<Exception> onHandlerError,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(onHandlerError);

        var handlers = _handlers;

        return handlers.Length == 0
            ? default
            : _SafeInvokeAsync(handlers, sender, eventArgs, onHandlerError, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IObserver<TEvent> observer)
    {
        Argument.IsNotNull(observer);

        return AddHandler(
            (_, args, _) =>
            {
                observer.OnNext(args);
                return default;
            }
        );
    }

    /// <inheritdoc />
    public void ClearHandlers()
    {
        lock (_lock)
        {
            _handlers = [];
        }
    }

    private static async ValueTask _InvokeSequentialAsync(
        Subscription[] handlers,
        object sender,
        TEvent eventArgs,
        CancellationToken cancellationToken
    )
    {
        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.Callback(sender, eventArgs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ValueTask _InvokeParallelAsync(
        Subscription[] handlers,
        object sender,
        TEvent eventArgs,
        CancellationToken cancellationToken
    )
    {
        // Start every handler; collect only the ones that did not complete synchronously (or completed faulted). When
        // all completed successfully in-line there is nothing to await — no Task.WhenAll, no allocation.
        List<Task>? pending = null;

        foreach (var handler in handlers)
        {
            ValueTask task;

            try
            {
                task = handler.Callback(sender, eventArgs, cancellationToken);
            }
            catch (Exception exception)
            {
                (pending ??= []).Add(Task.FromException(exception));
                continue;
            }

            if (task.IsCompletedSuccessfully)
            {
                continue;
            }

            (pending ??= []).Add(task.AsTask());
        }

        return pending is null ? default : new ValueTask(Task.WhenAll(pending).WithAggregatedExceptions());
    }

    private static async ValueTask _SafeInvokeAsync(
        Subscription[] handlers,
        object sender,
        TEvent eventArgs,
        Action<Exception> onHandlerError,
        CancellationToken cancellationToken
    )
    {
        foreach (var handler in handlers)
        {
            try
            {
                await handler.Callback(sender, eventArgs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                onHandlerError(exception);
            }
        }
    }

    private void _Remove(Subscription subscription)
    {
        lock (_lock)
        {
            var current = _handlers;
            var index = Array.IndexOf(current, subscription);

            if (index < 0)
            {
                return;
            }

            if (current.Length == 1)
            {
                _handlers = [];
                return;
            }

            var updated = new Subscription[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            _handlers = updated;
        }
    }

    // A single subscription is both the invocation-list entry (its identity distinguishes duplicate delegates) and the
    // IDisposable returned to the subscriber. Disposal is idempotent and thread-safe.
    private sealed class Subscription(AsyncEvent<TEvent> owner, AsyncEventHandler<TEvent> callback) : IDisposable
    {
        private AsyncEvent<TEvent>? _owner = owner;

        public AsyncEventHandler<TEvent> Callback { get; } = callback;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?._Remove(this);
        }
    }
}
