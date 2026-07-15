// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
