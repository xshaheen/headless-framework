// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks.Domains;

public interface ILocalMessage
{
    string MessageKey { get; }

    string Id { get; }

    DateTimeOffset Timestamp { get; }
}

public interface ILocalMessageHandler<in T>
    where T : class, ILocalMessage
{
    /// <summary>Handler handles the event by implementing this method.</summary>
    /// <param name="message">Message data</param>
    /// <param name="abortToken">Abort token</param>
    Task HandleAsync(T message, CancellationToken abortToken = default);
}

[PublicAPI]
[AttributeUsage(AttributeTargets.Class)]
public sealed class LocalEventHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>Handlers execute in ascending numeric value of the Order property.</summary>
    public int Order { get; } = order;
}
