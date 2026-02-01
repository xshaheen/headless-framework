// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Primitives;

namespace Tests.Entities;

/// <summary>
/// Full audit entity with all standard audit interfaces for comprehensive ORM testing.
/// </summary>
public sealed class HarnessTestEntity
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

    // Create audit
    public DateTimeOffset DateCreated { get; private init; }

    public UserId? CreatedById { get; private init; }

    // Update audit
    public DateTimeOffset? DateUpdated { get; private init; }

    public UserId? UpdatedById { get; private init; }

    // Delete audit
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private init; }

    public DateTimeOffset? DateRestored { get; private init; }

    public UserId? DeletedById { get; private init; }

    public UserId? RestoredById { get; private init; }

    // Suspend audit
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private init; }

    public UserId? SuspendedById { get; private init; }

    // Concurrency
    public string? ConcurrencyStamp { get; private init; }

    // Domain helpers to toggle flags so EF tracks modifications
    public void MarkDeleted() => IsDeleted = true;

    public void MarkRestored() => IsDeleted = false;

    public void MarkSuspended() => IsSuspended = true;

    public void MarkUnsuspended() => IsSuspended = false;

    public override IReadOnlyList<object> GetKeys() => [Id];
}
