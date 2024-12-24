// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.Disposables;

namespace Framework.Core;

/// <summary>Provides a set of static methods for creating <see cref="IDisposable" /> objects.</summary>
public static class DisposableFactory
{
    /// <summary>Gets the disposable that does nothing when disposed.</summary>
    public static IDisposable Empty => NoopDisposable.Instance;

    /// <summary>Gets the disposable that does nothing when disposed.</summary>
    public static IAsyncDisposable EmptyAsync => NoopDisposable.Instance;

    public static IDisposable Create(Action? dispose) => new Disposable(dispose);

    public static IAsyncDisposable Create(Func<Task> action) => new AsyncDisposable(async () => await action());

    public static IAsyncDisposable Create(Func<ValueTask> dispose) => new AsyncDisposable(dispose);
}
