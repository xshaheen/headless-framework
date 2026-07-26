using Headless.Domain;
using UserId = Headless.Primitives.UserId;

namespace Tests.Fixture;

public sealed class TestEntity
    : AggregateRoot,
        IEntity<Guid>,
        ICreateAudit<UserId>,
        IUpdateAudit<UserId>,
        IDeleteAudit<UserId>,
        ISuspendAudit<UserId>,
        IHasConcurrencyStamp,
        IMultiTenant
{
    public Guid Id { get; private init; }

    public required string Name { get; set; }

    public string? TenantId { get; init; }

    // Audits
    public DateTimeOffset CreatedAt { get; private init; }

    public UserId? CreatedById { get; private init; }

    public DateTimeOffset? UpdatedAt { get; private init; }

    public UserId? UpdatedById { get; private init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private init; }

    public DateTimeOffset? RestoredAt { get; private init; }

    public UserId? DeletedById { get; private init; }

    public UserId? RestoredById { get; private init; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private init; }

    public UserId? SuspendedById { get; private init; }

    public DateTimeOffset? UnsuspendedAt { get; private init; }

    public UserId? UnsuspendedById { get; private init; }

    // Concurrency
    public string? ConcurrencyStamp { get; private init; }

    // Domain helpers to toggle flags so EF tracks modifications
    public void MarkDeleted()
    {
        IsDeleted = true;
    }

    public void MarkSuspended()
    {
        IsSuspended = true;
    }

    // Domain behavior that raises events through the encapsulated (protected) aggregate mutators.
    public void EmitDomainEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }

    public void EmitIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }

    public override IReadOnlyList<object> GetKeys()
    {
        return [Id];
    }
}
