using Framework.Domain;
using Framework.Primitives;

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
    public DateTimeOffset DateCreated { get; private init; }

    public UserId? CreatedById { get; private init; }

    public DateTimeOffset? DateUpdated { get; private init; }

    public UserId? UpdatedById { get; private init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private init; }

    public DateTimeOffset? DateRestored { get; private init; }

    public UserId? DeletedById { get; private init; }

    public UserId? RestoredById { get; private init; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private init; }

    public UserId? SuspendedById { get; private init; }

    // Concurrency
    public string? ConcurrencyStamp { get; private init; }

    // Domain helpers to toggle flags so EF tracks modifications
    public void MarkDeleted() => IsDeleted = true;

    public void MarkSuspended() => IsSuspended = true;

    public override IReadOnlyList<object> GetKeys() => [Id];
}
