// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives.Internal;

namespace Headless.Primitives;

/// <summary>Represents an asynchronous event that can have multiple handlers.</summary>
/// <typeparam name="TEvent">The type of event arguments.</typeparam>
/// <remarks>
/// Unlike a delegate <see langword="event"/>, this invokes its handlers asynchronously (each may return a <see cref="ValueTask"/>),
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

    /// <summary>Adds an asynchronous handler that receives the event args and a cancellation token.</summary>
    /// <param name="callback">The handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes this specific registration when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(Func<TEvent, CancellationToken, ValueTask> callback);

    /// <summary>Adds a synchronous handler that receives the event args.</summary>
    /// <param name="callback">The handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes this specific registration when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(Action<TEvent> callback);

    /// <summary>Adds an asynchronous handler that also receives the sender.</summary>
    /// <param name="callback">The handler to add.</param>
    /// <returns>An <see cref="IDisposable"/> that removes this specific registration when disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is <see langword="null"/>.</exception>
    IDisposable AddHandler(AsyncEventHandler<TEvent> callback);

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
