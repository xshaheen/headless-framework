// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Domain;

/// <summary>
/// Provides a base implementation for entities with a single primary key that carry creation and update audit
/// fields.
/// </summary>
/// <remarks>
/// Setters are <see langword="protected"/> so only entity domain methods and the persistence layer can mutate audit
/// state. Derive from <see cref="SuspendableEntity{TId}"/> or <see cref="SoftDeletableEntity{TId}"/>
/// instead when the entity also needs suspension or soft delete.
/// </remarks>
/// <typeparam name="TId">The primary key type.</typeparam>
[PublicAPI]
public abstract class AuditedEntity<TId> : Entity<TId>, ICreateAudit, IUpdateAudit
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
}

/// <summary>
/// Provides a base implementation for audited entities that also record the account identifier of the creator
/// and the last updater.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public abstract class AuditedEntity<TId, TAccountId>
    : AuditedEntity<TId>,
        ICreateAudit<TAccountId>,
        IUpdateAudit<TAccountId>
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
}

/// <summary>
/// Provides a base implementation for audited entities that record account navigation references for the
/// creator and the last updater, and expose the <see cref="Update"/> transition.
/// </summary>
/// <typeparam name="TId">The primary key type.</typeparam>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public abstract class AuditedEntity<TId, TAccountId, TAccount>
    : AuditedEntity<TId, TAccountId>,
        ICreateAudit<TAccountId, TAccount>,
        IUpdateAudit<TAccountId, TAccount>
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
    public virtual void Update(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default)
    {
        UpdatedAt = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}
