namespace Framework.BuildingBlocks.Helpers;

public sealed class NullDisposable : IDisposable
{
    private NullDisposable() { }

    public void Dispose() { }

    public static NullDisposable Instance { get; } = new();
}
