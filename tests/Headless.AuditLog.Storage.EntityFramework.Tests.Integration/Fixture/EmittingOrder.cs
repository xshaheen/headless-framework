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
    private readonly List<EventOccurrence<IIntegrationEvent>> _messages = [];

    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public void AddIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _messages.Add(EventOccurrence.Capture<IIntegrationEvent>(integrationEvent));
    }

    public void ClearIntegrationEvents()
    {
        _messages.Clear();
    }

    public IReadOnlyList<EventOccurrence<IIntegrationEvent>> GetIntegrationEvents()
    {
        return _messages;
    }

    public void AddIntegrationEvent(EventOccurrence<IIntegrationEvent> occurrence) => _messages.Add(occurrence);

    public void ClearIntegrationEvents(IReadOnlyList<EventOccurrence<IIntegrationEvent>> occurrences)
    {
        var ids = occurrences.Select(occurrence => occurrence.Context.EventId).ToHashSet(StringComparer.Ordinal);
        _messages.RemoveAll(occurrence => ids.Contains(occurrence.Context.EventId));
    }

    public void Emit(IIntegrationEvent message)
    {
        AddIntegrationEvent(message);
    }
}

internal sealed record TestDistributedMessage(string UniqueId) : IIntegrationEvent;
