namespace Framework.BuildingBlocks.Helpers;

public sealed class NullAsyncDisposable : IAsyncDisposable
{
    private NullAsyncDisposable() { }

    public ValueTask DisposeAsync() => default;

    public static NullAsyncDisposable Instance { get; } = new();
}
