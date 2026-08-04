// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Nito.Disposables;

namespace Headless.Core;

/// <summary>Provides a set of static methods for creating <see cref="IDisposable" /> objects.</summary>
public static class DisposableFactory
{
    /// <summary>Gets the disposable that does nothing when disposed.</summary>
    public static IDisposable Empty => NoopDisposable.Instance;

    /// <summary>Gets the disposable that does nothing when disposed.</summary>
    public static IAsyncDisposable EmptyAsync => NoopDisposable.Instance;

    /// <summary>Creates an <see cref="IDisposable"/> that runs <paramref name="dispose"/> when disposed.</summary>
    /// <param name="dispose">The action to invoke on disposal; <see langword="null"/> produces a no-op disposable.</param>
    /// <returns>An <see cref="IDisposable"/> wrapping <paramref name="dispose"/>.</returns>
    public static IDisposable Create(Action? dispose)
    {
        return new Disposable(dispose);
    }

    /// <summary>Creates an <see cref="IAsyncDisposable"/> that awaits <paramref name="action"/> when disposed.</summary>
    /// <param name="action">The asynchronous callback to invoke on disposal.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> wrapping <paramref name="action"/>.</returns>
    public static IAsyncDisposable Create(Func<Task> action)
    {
        return new AsyncDisposable(async () => await action().ConfigureAwait(false));
    }

    /// <summary>Creates an <see cref="IAsyncDisposable"/> that awaits <paramref name="dispose"/> when disposed.</summary>
    /// <param name="dispose">The asynchronous callback to invoke on disposal.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> wrapping <paramref name="dispose"/>.</returns>
    public static IAsyncDisposable Create(Func<ValueTask> dispose)
    {
        return new AsyncDisposable(dispose);
    }

    /// <summary>
    /// Creates an <see cref="IDisposable"/> that invokes <paramref name="dispose"/> with <paramref name="state"/>
    /// when disposed. Pass a <see langword="static"/> lambda to avoid the display-class and delegate allocations
    /// the closure-based <see cref="Create(Action?)"/> overload incurs on hot paths.
    /// </summary>
    /// <param name="state">The state passed to <paramref name="dispose"/> on disposal.</param>
    /// <param name="dispose">The callback to invoke exactly once on disposal.</param>
    /// <returns>An <see cref="IDisposable"/> invoking <paramref name="dispose"/> with <paramref name="state"/>.</returns>
    public static IDisposable Create<TState>(TState state, Action<TState> dispose)
    {
        Argument.IsNotNull(dispose);

        return new StateDisposable<TState>(state, dispose);
    }

    /// <summary>
    /// Creates an <see cref="IAsyncDisposable"/> that awaits <paramref name="dispose"/> with <paramref name="state"/>
    /// when disposed. Pass a <see langword="static"/> lambda to avoid the display-class and delegate allocations
    /// the closure-based <see cref="Create(Func{ValueTask})"/> overload incurs on hot paths.
    /// </summary>
    /// <param name="state">The state passed to <paramref name="dispose"/> on disposal.</param>
    /// <param name="dispose">The asynchronous callback to invoke exactly once on disposal.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> awaiting <paramref name="dispose"/> with <paramref name="state"/>.</returns>
    public static IAsyncDisposable Create<TState>(TState state, Func<TState, ValueTask> dispose)
    {
        Argument.IsNotNull(dispose);

        return new StateAsyncDisposable<TState>(state, dispose);
    }

    private sealed class StateDisposable<TState>(TState state, Action<TState> dispose)
        : SingleDisposable<(TState State, Action<TState> Dispose)>((state, dispose))
    {
        protected override void Dispose((TState State, Action<TState> Dispose) context)
        {
            context.Dispose(context.State);
        }
    }

    private sealed class StateAsyncDisposable<TState>(TState state, Func<TState, ValueTask> dispose)
        : SingleAsyncDisposable<(TState State, Func<TState, ValueTask> Dispose)>((state, dispose))
    {
        protected override ValueTask DisposeAsync((TState State, Func<TState, ValueTask> Dispose) context)
        {
            return context.Dispose(context.State);
        }
    }
}
