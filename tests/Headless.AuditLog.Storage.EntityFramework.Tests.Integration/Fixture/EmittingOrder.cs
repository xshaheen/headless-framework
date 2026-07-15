using Headless.AuditLog;
using Headless.Domain;

namespace Tests.Fixture;

/// <summary>
/// Audit-tracked entity that ALSO emits a distributed message. Required to exercise the
/// post-persist failure path: the runtime only invokes the distributed enqueue callback
/// when at least one emitter is present, and the catch-time discard only fires
/// after audit persistence has populated <c>auditSave.AuditEntries</c>.
/// </summary>
public sealed class EmittingOrder : IAuditTracked, IIntegrationEventEmitter
{
    private readonly List<IIntegrationEvent> _messages = [];

    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public void AddIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _messages.Add(integrationEvent);
    }

    public void ClearIntegrationEvents()
    {
        _messages.Clear();
    }

    public IReadOnlyList<IIntegrationEvent> GetIntegrationEvents()
    {
        return _messages;
    }

    public void Emit(IIntegrationEvent message)
    {
        _messages.Add(message);
    }
}

internal sealed record TestDistributedMessage(string UniqueId) : IIntegrationEvent;
