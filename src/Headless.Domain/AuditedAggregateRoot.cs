// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Domain;

/// <summary>
/// Provides a base implementation for aggregate roots with a single primary key that carry creation and update audit
/// fields.
/// </summary>
/// <remarks>
/// Setters are <see langword="protected"/> so only aggregate domain methods and the persistence layer can mutate audit
/// state. Derive from <see cref="SoftDeletableAggregateRoot{TId}"/> instead when the aggregate also needs soft delete.
/// </remarks>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public abstract class AuditedAggregateRoot<TId> : AggregateRoot<TId>, ICreateAudit, IUpdateAudit
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
}

/// <summary>
/// Provides a base implementation for audited aggregate roots that also record the account identifier of the creator
/// and the last updater.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public abstract class AuditedAggregateRoot<TId, TAccountId>
    : AuditedAggregateRoot<TId>,
        ICreateAudit<TAccountId>,
        IUpdateAudit<TAccountId>
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
}

/// <summary>
/// Provides a base implementation for audited aggregate roots that record account navigation references for the
/// creator and the last updater, and expose the <see cref="Update"/> transition.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public abstract class AuditedAggregateRoot<TId, TAccountId, TAccount>
    : AuditedAggregateRoot<TId, TAccountId>,
        ICreateAudit<TAccountId, TAccount>,
        IUpdateAudit<TAccountId, TAccount>
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
    public virtual void Update(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default)
    {
        UpdatedAt = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}
