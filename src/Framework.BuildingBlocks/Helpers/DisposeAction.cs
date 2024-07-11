namespace Framework.BuildingBlocks.Helpers;

/// <summary>This class can be used to provide an action when Dispose method is called.</summary>
/// <param name="action">Action to be executed when this object is disposed.</param>
[PublicAPI]
public sealed class DisposeAction(Action action) : IDisposable
{
    private readonly Action _action = Argument.IsNotNull(action);

    public void Dispose() => _action();
}
