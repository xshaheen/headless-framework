// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Domain;

/// <summary>
/// Provides a base implementation for entities with a single primary key that carry creation, update, suspension, and soft-delete audit fields.
/// </summary>
/// <remarks>
/// Setters are <see langword="protected"/> so only entity domain methods and the persistence layer can mutate audit state.
/// </remarks>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public abstract class AuditedEntity<TId> : Entity<TId>, ICreateAudit, IUpdateAudit, ISuspendAudit, IDeleteAudit
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AuditedEntity{TId}"/> class.</summary>
    protected AuditedEntity() { }

    /// <summary>Initializes a new instance of the <see cref="AuditedEntity{TId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AuditedEntity(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? UpdatedAt { get; protected set; }

    /// <inheritdoc/>
    public bool IsSuspended { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? SuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? UnsuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public bool IsDeleted { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? RestoredAt { get; protected set; }
}

/// <summary>
/// Provides a base implementation for audited entities that also record the account identifier for audit transitions.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public abstract class AuditedEntity<TId, TAccountId>
    : AuditedEntity<TId>,
        ICreateAudit<TAccountId>,
        IUpdateAudit<TAccountId>,
        ISuspendAudit<TAccountId>,
        IDeleteAudit<TAccountId>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AuditedEntity{TId, TAccountId}"/> class.</summary>
    protected AuditedEntity() { }

    /// <summary>Initializes a new instance of the <see cref="AuditedEntity{TId, TAccountId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AuditedEntity(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public TAccountId? CreatedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? UpdatedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? SuspendedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? UnsuspendedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? DeletedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? RestoredById { get; protected set; }
}

/// <summary>
/// Provides a base implementation for audited entities that record account navigation references
/// and expose suspension and soft-delete transitions.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public abstract class AuditedEntity<TId, TAccountId, TAccount>
    : AuditedEntity<TId, TAccountId>,
        ICreateAudit<TAccountId, TAccount>,
        IUpdateAudit<TAccountId, TAccount>,
        ISuspendAudit<TAccountId, TAccount>,
        IDeleteAudit<TAccountId, TAccount>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AuditedEntity{TId, TAccountId, TAccount}"/> class.</summary>
    protected AuditedEntity() { }

    /// <summary>Initializes a new instance of the <see cref="AuditedEntity{TId, TAccountId, TAccount}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AuditedEntity(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public TAccount CreatedBy { get; protected set; } = default!;

    /// <inheritdoc/>
    public TAccount? UpdatedBy { get; protected set; }

    /// <inheritdoc/>
    public TAccount? SuspendedBy { get; protected set; }

    /// <inheritdoc/>
    public TAccount? UnsuspendedBy { get; protected set; }

    /// <inheritdoc/>
    public TAccount? DeletedBy { get; protected set; }

    /// <inheritdoc/>
    public TAccount? RestoredBy { get; protected set; }

    /// <inheritdoc/>
    /// <remarks>Does nothing when the entity is already suspended, so the original suspension audit is kept.</remarks>
    public virtual void Suspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default)
    {
        if (IsSuspended)
        {
            return;
        }

        IsSuspended = true;
        SuspendedAt = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    /// <inheritdoc/>
    /// <remarks>Does nothing when the entity is not suspended.</remarks>
    public virtual void Unsuspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default)
    {
        if (!IsSuspended)
        {
            return;
        }

        IsSuspended = false;
        SuspendedAt = null;
        SuspendedById = default;
        SuspendedBy = default;
        UnsuspendedAt = now;
        UnsuspendedById = byId;
        UnsuspendedBy = by;
    }

    /// <inheritdoc/>
    /// <remarks>Does nothing when the entity is already deleted, so the original deletion audit is kept.</remarks>
    public virtual void Delete(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    /// <inheritdoc/>
    /// <remarks>Does nothing when the entity is not deleted.</remarks>
    public virtual void Restore(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default)
    {
        if (!IsDeleted)
        {
            return;
        }

        IsDeleted = false;
        DeletedAt = null;
        DeletedById = default;
        DeletedBy = default;
        RestoredAt = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}
