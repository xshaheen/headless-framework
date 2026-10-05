// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Domain;

/// <summary>
/// Provides a base implementation for audited entities with a single primary key that are soft-deleted instead
/// of removed.
/// </summary>
/// <remarks>
/// Soft-deleted rows are hidden from queries by the not-deleted query filter; bypass it to find a row to restore. The
/// deletion and restoration fields each keep the most recent transition, so restoring the entity does not erase
/// who deleted it and when.
/// </remarks>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public abstract class SoftDeletableEntity<TId> : AuditedEntity<TId>, IDeleteAudit
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="SoftDeletableEntity{TId}"/> class.</summary>
    protected SoftDeletableEntity() { }

    /// <summary>Initializes a new instance of the <see cref="SoftDeletableEntity{TId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected SoftDeletableEntity(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public bool IsDeleted { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? RestoredAt { get; protected set; }
}

/// <summary>
/// Provides a base implementation for soft-deletable audited entities that also record the account identifier
/// for every audit transition.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public abstract class SoftDeletableEntity<TId, TAccountId> : AuditedEntity<TId, TAccountId>, IDeleteAudit<TAccountId>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="SoftDeletableEntity{TId, TAccountId}"/> class.</summary>
    protected SoftDeletableEntity() { }

    /// <summary>Initializes a new instance of the <see cref="SoftDeletableEntity{TId, TAccountId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected SoftDeletableEntity(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public bool IsDeleted { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? RestoredAt { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? DeletedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? RestoredById { get; protected set; }
}

/// <summary>
/// Provides a base implementation for soft-deletable audited entities that record account navigation
/// references and expose the update, delete, and restore transitions.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public abstract class SoftDeletableEntity<TId, TAccountId, TAccount>
    : AuditedEntity<TId, TAccountId, TAccount>,
        IDeleteAudit<TAccountId, TAccount>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="SoftDeletableEntity{TId, TAccountId, TAccount}"/> class.</summary>
    protected SoftDeletableEntity() { }

    /// <summary>Initializes a new instance of the <see cref="SoftDeletableEntity{TId, TAccountId, TAccount}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected SoftDeletableEntity(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public bool IsDeleted { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? RestoredAt { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? DeletedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? RestoredById { get; protected set; }

    /// <inheritdoc/>
    public TAccount? DeletedBy { get; protected set; }

    /// <inheritdoc/>
    public TAccount? RestoredBy { get; protected set; }

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
        RestoredAt = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}
