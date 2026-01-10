namespace Framework.Domain.Messaging;

/// <summary>
/// Base consumer interface shared between domain message handlers
/// and infrastructure message handlers.
/// </summary>
public interface IConsumer<in TMessage>
{
    Task ConsumeAsync(TMessage message, CancellationToken ct = default);
}
