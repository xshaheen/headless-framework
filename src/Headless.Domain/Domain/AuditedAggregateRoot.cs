// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Base class for aggregate roots with a single primary key that carry create, update, suspend, and soft-delete audit fields.
/// </summary>
/// <remarks>
/// Setters are <see langword="protected"/> so only the aggregate's own behavior and the persistence layer, which
/// writes non-public setters through reflection, can change the audit state.
/// </remarks>
/// <typeparam name="TId">Type of the primary key of the entity.</typeparam>
[PublicAPI]
public abstract class AuditedAggregateRoot<TId>
    : AggregateRoot<TId>,
        ICreateAudit,
        IUpdateAudit,
        ISuspendAudit,
        IDeleteAudit
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AuditedAggregateRoot{TId}"/> class.</summary>
    protected AuditedAggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="AuditedAggregateRoot{TId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AuditedAggregateRoot(TId id)
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
/// Base class for audited aggregate roots that also record the identifier of the account behind each audit transition.
/// </summary>
/// <typeparam name="TId">Type of the primary key of the entity.</typeparam>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
[PublicAPI]
public abstract class AuditedAggregateRoot<TId, TAccountId>
    : AuditedAggregateRoot<TId>,
        ICreateAudit<TAccountId>,
        IUpdateAudit<TAccountId>,
        ISuspendAudit<TAccountId>,
        IDeleteAudit<TAccountId>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AuditedAggregateRoot{TId, TAccountId}"/> class.</summary>
    protected AuditedAggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="AuditedAggregateRoot{TId, TAccountId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AuditedAggregateRoot(TId id)
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
/// Base class for audited aggregate roots that also carry a navigation link to the account behind each audit transition,
/// and expose the suspend and soft-delete transitions.
/// </summary>
/// <typeparam name="TId">Type of the primary key of the entity.</typeparam>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
/// <typeparam name="TAccount">Type of the account entity.</typeparam>
[PublicAPI]
public abstract class AuditedAggregateRoot<TId, TAccountId, TAccount>
    : AuditedAggregateRoot<TId, TAccountId>,
        ICreateAudit<TAccountId, TAccount>,
        IUpdateAudit<TAccountId, TAccount>,
        ISuspendAudit<TAccountId, TAccount>,
        IDeleteAudit<TAccountId, TAccount>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="AuditedAggregateRoot{TId, TAccountId, TAccount}"/> class.</summary>
    protected AuditedAggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="AuditedAggregateRoot{TId, TAccountId, TAccount}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected AuditedAggregateRoot(TId id)
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
    /// <remarks>Does nothing when the aggregate is already suspended, so the original suspension audit is kept.</remarks>
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
    /// <remarks>Does nothing when the aggregate is not suspended.</remarks>
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
    /// <remarks>Does nothing when the aggregate is already deleted, so the original deletion audit is kept.</remarks>
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
    /// <remarks>Does nothing when the aggregate is not deleted.</remarks>
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
