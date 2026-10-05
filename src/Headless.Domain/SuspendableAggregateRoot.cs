// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Domain;

/// <summary>
/// Provides a base implementation for audited aggregate roots with a single primary key that can also be suspended.
/// </summary>
/// <remarks>
/// Suspension is a business state, not a visibility rule: suspended rows stay visible to queries unless the entity
/// type opts into the not-suspended query filter. The suspension and unsuspension fields each keep the most recent
/// transition, so lifting a suspension does not erase who suspended the aggregate and when.
/// </remarks>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public abstract class SuspendableAggregateRoot<TId> : AuditedAggregateRoot<TId>, ISuspendAudit
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="SuspendableAggregateRoot{TId}"/> class.</summary>
    protected SuspendableAggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="SuspendableAggregateRoot{TId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected SuspendableAggregateRoot(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public bool IsSuspended { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? SuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? UnsuspendedAt { get; protected set; }
}

/// <summary>
/// Provides a base implementation for suspendable audited aggregate roots that also record the account identifier
/// for every audit transition.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public abstract class SuspendableAggregateRoot<TId, TAccountId>
    : AuditedAggregateRoot<TId, TAccountId>,
        ISuspendAudit<TAccountId>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="SuspendableAggregateRoot{TId, TAccountId}"/> class.</summary>
    protected SuspendableAggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="SuspendableAggregateRoot{TId, TAccountId}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected SuspendableAggregateRoot(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public bool IsSuspended { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? SuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? UnsuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? SuspendedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? UnsuspendedById { get; protected set; }
}

/// <summary>
/// Provides a base implementation for suspendable audited aggregate roots that record account navigation references
/// and expose the update and suspension transitions.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public abstract class SuspendableAggregateRoot<TId, TAccountId, TAccount>
    : AuditedAggregateRoot<TId, TAccountId, TAccount>,
        ISuspendAudit<TAccountId, TAccount>
    where TId : IEquatable<TId>
{
    /// <summary>Initializes a new instance of the <see cref="SuspendableAggregateRoot{TId, TAccountId, TAccount}"/> class.</summary>
    protected SuspendableAggregateRoot() { }

    /// <summary>Initializes a new instance of the <see cref="SuspendableAggregateRoot{TId, TAccountId, TAccount}"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier for this entity.</param>
    [SetsRequiredMembers]
    protected SuspendableAggregateRoot(TId id)
        : base(id) { }

    /// <inheritdoc/>
    public bool IsSuspended { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? SuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public DateTimeOffset? UnsuspendedAt { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? SuspendedById { get; protected set; }

    /// <inheritdoc/>
    public TAccountId? UnsuspendedById { get; protected set; }

    /// <inheritdoc/>
    public TAccount? SuspendedBy { get; protected set; }

    /// <inheritdoc/>
    public TAccount? UnsuspendedBy { get; protected set; }

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
        UnsuspendedAt = now;
        UnsuspendedById = byId;
        UnsuspendedBy = by;
    }
}
