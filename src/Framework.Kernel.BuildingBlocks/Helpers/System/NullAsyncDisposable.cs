namespace Framework.Kernel.BuildingBlocks.Helpers.System;

[PublicAPI]
public sealed class NullAsyncDisposable : IAsyncDisposable
{
    private NullAsyncDisposable() { }

    public ValueTask DisposeAsync() => default;

    public static NullAsyncDisposable Instance { get; } = new();
}
