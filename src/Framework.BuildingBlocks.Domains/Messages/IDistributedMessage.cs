// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface IDistributedMessage
{
    string MessageKey { get; }

    string Id { get; }

    DateTimeOffset Timestamp { get; }
}

public interface IDistributedMessageHandler<in T>
    where T : class, IDistributedMessage
{
    /// <summary>Handler handles the event by implementing this method.</summary>
    /// <param name="message">Event data</param>
    /// <param name="abortToken">Abort token</param>
    ValueTask HandleAsync(T message, CancellationToken abortToken = default);
}
