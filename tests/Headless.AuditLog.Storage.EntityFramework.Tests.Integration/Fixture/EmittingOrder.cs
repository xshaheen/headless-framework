using Headless.Domain;

namespace Tests.Fixture;

/// <summary>
/// Audited entity that ALSO emits a distributed message. Required to exercise the
/// post-persist failure path: the runtime only invokes the distributed enqueue callback
/// when at least one emitter is present, and the catch-time discard only fires
/// after audit persistence has populated <c>auditSave.AuditEntries</c>.
/// </summary>
public sealed class EmittingOrder : IIntegrationEventEmitter
{
    private readonly List<EventContext<object>> _messages = [];

    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public void AddIntegrationEvent(object integrationEvent)
    {
        _messages.Add(EventContext.Capture<object>(integrationEvent));
    }

    public void ClearIntegrationEvents()
    {
        _messages.Clear();
    }

    public IReadOnlyList<EventContext<object>> GetIntegrationEvents()
    {
        return _messages;
    }

    public void AddIntegrationEvent<TPayload>(EventContext<TPayload> context)
        where TPayload : class =>
        _messages.Add(
            context as EventContext<object>
                ?? new(context.Payload, context.EventId, context.CorrelationId, context.CausationId, context.TenantId)
        );

    public void ClearIntegrationEvents(IReadOnlyList<EventContext<object>> occurrences)
    {
        var ids = occurrences.Select(occurrence => occurrence.EventId).ToHashSet(StringComparer.Ordinal);
        _messages.RemoveAll(occurrence => ids.Contains(occurrence.EventId));
    }

    public void Emit(object message)
    {
        AddIntegrationEvent(message);
    }
}

internal sealed record TestDistributedMessage(string UniqueId);
