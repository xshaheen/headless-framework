// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines soft-delete audit fields for an entity.</summary>
[PublicAPI]
public interface IDeleteAudit
{
    /// <summary>Gets a value indicating whether this entity is soft-deleted.</summary>
    bool IsDeleted { get; }

    /// <summary>Gets the date and time when the entity was most recently soft-deleted.</summary>
    /// <remarks>
    /// Populated automatically by persistence infrastructure. Kept after the entity is restored, so read
    /// <see cref="IsDeleted"/>, not this value, for the current state.
    /// </remarks>
    DateTimeOffset? DeletedAt { get; }

    /// <summary>Gets the date and time when the entity was most recently restored.</summary>
    /// <remarks>Populated automatically by persistence infrastructure. Kept after a later deletion.</remarks>
    DateTimeOffset? RestoredAt { get; }
}

/// <summary>Defines soft-delete audit fields that include identifiers for the accounts that deleted and restored the entity.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public interface IDeleteAudit<out TAccountId> : IDeleteAudit
{
    /// <summary>Gets the identifier of the account that most recently soft-deleted this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? DeletedById { get; }

    /// <summary>Gets the identifier of the account that most recently restored this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? RestoredById { get; }
}

/// <summary>
/// Defines soft-delete audit fields with navigation references to the accounts that soft-deleted
/// and restored the entity, and methods to transition between deletion states.
/// </summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public interface IDeleteAudit<TAccountId, TAccount> : IDeleteAudit<TAccountId>
{
    /// <summary>Gets the navigation reference to the account that most recently soft-deleted this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount? DeletedBy { get; }

    /// <summary>Gets the navigation reference to the account that most recently restored this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount? RestoredBy { get; }

    /// <summary>Marks the entity as soft-deleted, recording the timestamp and the responsible account.</summary>
    /// <param name="now">The UTC timestamp of the deletion.</param>
    /// <param name="byId">The identifier of the account performing the deletion, or <see langword="null"/> if unknown.</param>
    /// <param name="by">The navigation reference to the account performing the deletion, or <see langword="null"/> if not loaded.</param>
    void Delete(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    /// <summary>
    /// Restores a soft-deleted entity and records the restoration timestamp and account. The deletion fields are kept
    /// as history.
    /// </summary>
    /// <param name="now">The UTC timestamp of the restoration.</param>
    /// <param name="byId">The identifier of the account performing the restoration, or <see langword="null"/> if unknown.</param>
    /// <param name="by">The navigation reference to the account performing the restoration, or <see langword="null"/> if not loaded.</param>
    void Restore(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
