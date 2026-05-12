using Headless.AuditLog;
using Headless.Domain;

namespace Tests.Fixture;

/// <summary>
/// Audit-tracked entity that ALSO emits a distributed message. Required to exercise the
/// post-persist failure path: the runtime only invokes the distributed enqueue callback
/// when at least one emitter is present, and the catch-time discard only fires
/// after audit persistence has populated <c>auditSave.AuditEntries</c>.
/// </summary>
public sealed class EmittingOrder : IAuditTracked, IDistributedMessageEmitter
{
    private readonly List<IDistributedMessage> _messages = [];

    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public void AddMessage(IDistributedMessage message)
    {
        _messages.Add(message);
    }

    public void ClearDistributedMessages()
    {
        _messages.Clear();
    }

    public IReadOnlyList<IDistributedMessage> GetDistributedMessages() => _messages;

    public void Emit(IDistributedMessage message) => _messages.Add(message);
}

internal sealed record TestDistributedMessage(string UniqueId) : IDistributedMessage;
