// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Core;

/// <summary>Provides a set of static methods for creating <see cref="IDisposable" /> objects.</summary>
public static class Disposable
{
    /// <summary>Gets the disposable that does nothing when disposed.</summary>
    public static IDisposable Empty { get; } = new EmptyDisposable();

    /// <summary>Gets the disposable that does nothing when disposed.</summary>
    public static IAsyncDisposable EmptyAsync { get; } = new EmptyAsyncDisposable();

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

    public static IAsyncDisposable Create(Func<ValueTask> action)
    {
        return new AsyncValueDisposableAction(action);
    }

    public static IAsyncDisposable Create<TState>(TState parameter, Func<TState, ValueTask> action)
    {
        return new AsyncValueDisposableAction<TState>(action, parameter);
    }

    #region Helpers

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class EmptyAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisposeAction(Action action) : IDisposable
    {
        private readonly Action _action = Argument.IsNotNull(action);

        public void Dispose() => _action();
    }

    private sealed class DisposeAction<T>(Action<T> action, T state) : IDisposable
    {
        private readonly Action<T> _action = Argument.IsNotNull(action);

        private readonly T? _state = state;

        public void Dispose()
        {
            if (_state is not null)
            {
                _action(_state);
            }
        }
    }

    private sealed class AsyncDisposableAction(Func<Task> action) : IAsyncDisposable
    {
        private Func<Task>? _action = action;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            var exitAction = Interlocked.Exchange(ref _action, value: null);

            if (exitAction is not null)
            {
                await exitAction();
            }
        }
    }

    private sealed class AsyncDisposableAction<T>(Func<T, Task> action, T state) : IAsyncDisposable
    {
        private Func<T, Task>? _action = action;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            var exitAction = Interlocked.Exchange(ref _action, value: null);

            if (exitAction is not null)
            {
                await exitAction(state);
            }
        }
    }

    private sealed class AsyncValueDisposableAction(Func<ValueTask> action) : IAsyncDisposable
    {
        private Func<ValueTask>? _action = action;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            var exitAction = Interlocked.Exchange(ref _action, value: null);

            if (exitAction is not null)
            {
                await exitAction();
            }
        }
    }

    private sealed class AsyncValueDisposableAction<T>(Func<T, ValueTask> action, T state) : IAsyncDisposable
    {
        private Func<T, ValueTask>? _action = action;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            var exitAction = Interlocked.Exchange(ref _action, value: null);

            if (exitAction is not null)
            {
                await exitAction(state);
            }
        }
    }

    #endregion
}
