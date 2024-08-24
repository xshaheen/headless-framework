namespace Framework.BuildingBlocks.Helpers;

/// <summary>This class can be used to provide an action when the Dispose method is called.</summary>
/// <param name="action">Action to be executed when this object is disposed.</param>
[PublicAPI]
public sealed class DisposeAction(Action action) : IDisposable
{
    private readonly Action _action = Argument.IsNotNull(action);

    public void Dispose() => _action();
}

[PublicAPI]
public sealed class DisposeAction<TState>(Action<TState> action, TState parameter) : IDisposable
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
