// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.BuildingBlocks.Helpers.System;

public static class Disposable
{
    public static readonly IDisposable Empty = new EmptyDisposable();

    public static IDisposable Create(Action action) => new DisposeAction(action);

    public static IDisposable Create<TState>(TState parameter, Action<TState> action)
    {
        return new DisposeAction<TState>(action, parameter);
    }

    public static IAsyncDisposable Create(Func<Task> action)
    {
        return new AsyncDisposableAction(action);
    }

    public static IAsyncDisposable Create<TState>(TState parameter, Func<TState, Task> action)
    {
        return new AsyncDisposableAction<TState>(action, parameter);
    }
}

file sealed class EmptyDisposable : IDisposable
{
    public void Dispose() { }
}

/// <summary>This class can be used to provide an action when the Dispose method is called.</summary>
/// <param name="action">Action to be executed when this object is disposed.</param>
file sealed class DisposeAction(Action action) : IDisposable
{
    private readonly Action _action = Argument.IsNotNull(action);

    public void Dispose() => _action();
}

file sealed class DisposeAction<TState>(Action<TState> action, TState parameter) : IDisposable
{
    private readonly Action<TState> _action = Argument.IsNotNull(action);

    private readonly TState? _parameter = parameter;

    public void Dispose()
    {
        if (_parameter is not null)
        {
            _action(_parameter);
        }
    }
}

/// <summary>A class that will call an <see cref="Func{TResult}"/> when Disposed.</summary>
/// <remarks>Initializes a new instance of the <see cref="AsyncDisposableAction"/> class.</remarks>
/// <param name="exitTask">The exit action.</param>
file sealed class AsyncDisposableAction(Func<Task> exitTask) : IAsyncDisposable
{
    private Func<Task>? _exitTask = exitTask;

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        var exitAction = Interlocked.Exchange(ref _exitTask, value: null);

        if (exitAction is not null)
        {
            await exitAction().AnyContext();
        }
    }
}

/// <summary>A class that will call an <see cref="Func{TResult}"/> when Disposed.</summary>
/// <remarks>Initializes a new instance of the <see cref="AsyncDisposableAction"/> class.</remarks>
/// <param name="exitTask">The exit action.</param>
file sealed class AsyncDisposableAction<TState>(Func<TState, Task> exitTask, TState parameter) : IAsyncDisposable
{
    private Func<TState, Task>? _exitTask = exitTask;

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        var exitAction = Interlocked.Exchange(ref _exitTask, value: null);

        if (exitAction is not null)
        {
            await exitAction(parameter).AnyContext();
        }
    }
}
