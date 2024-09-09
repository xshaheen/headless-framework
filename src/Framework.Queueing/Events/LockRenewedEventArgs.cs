// ReSharper disable once CheckNamespace
namespace Framework.Queueing;

public sealed class LockRenewedEventArgs<T> : EventArgs
    where T : class
{
    public required IQueue<T> Queue { get; init; }

    public required IQueueEntry<T> Entry { get; init; }
}
