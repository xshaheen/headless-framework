// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

public static class Disposable
{
    public static readonly IDisposable Empty = new EmptyDisposable();

    public static IDisposable Create(Action action) => new DisposeAction(action);

    public static IDisposable Create<TState>(TState parameter, Action<TState> action)
    {
        return new DisposeAction<TState>(action, parameter);
    }
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

file sealed class EmptyDisposable : IDisposable
{
    public void Dispose() { }
}
