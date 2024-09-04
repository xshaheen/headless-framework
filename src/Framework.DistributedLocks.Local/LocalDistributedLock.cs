namespace Framework.DistributedLocks.Local;

[PublicAPI]
public sealed class LocalDistributedLock(string resource, IDisposable disposable) : IDistributedLock
{
    public string Resource { get; } = resource;

    public ValueTask DisposeAsync()
    {
        disposable.Dispose();
        return ValueTask.CompletedTask;
    }
}
