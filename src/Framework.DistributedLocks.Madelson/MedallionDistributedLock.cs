using Medallion.Threading;

namespace Framework.DistributedLocks.Madelson;

[PublicAPI]
public sealed class MedallionDistributedLock(string resource, IDistributedSynchronizationHandle handle)
    : IDistributedLock
{
    public string Resource { get; } = resource;

    public IDistributedSynchronizationHandle Handle { get; } = handle;

    public ValueTask DisposeAsync() => Handle.DisposeAsync();
}
