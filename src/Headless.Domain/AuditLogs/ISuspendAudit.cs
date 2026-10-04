// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines suspension audit fields for an entity.</summary>
[PublicAPI]
public interface ISuspendAudit
{
    /// <summary>Gets a value indicating whether this entity is suspended.</summary>
    bool IsSuspended { get; }

    /// <summary>Gets the date and time when the entity was suspended.</summary>
    /// <remarks>Populated automatically by persistence infrastructure. A non-null value indicates the entity is suspended.</remarks>
    DateTimeOffset? SuspendedAt { get; }

    /// <summary>Gets the date and time when the entity was unsuspended.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    DateTimeOffset? UnsuspendedAt { get; }
}

/// <summary>Defines suspension audit fields that include identifiers for the accounts that suspended and unsuspended the entity.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public interface ISuspendAudit<out TAccountId> : ISuspendAudit
{
    /// <summary>Gets the identifier of the account that suspended this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? SuspendedById { get; }

    /// <summary>Gets the identifier of the account that unsuspended this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? UnsuspendedById { get; }
}

/// <summary>
/// Defines suspension audit fields with navigation references to the accounts that suspended
/// and unsuspended the entity, and methods to transition between suspension states.
/// </summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public interface ISuspendAudit<TAccountId, TAccount> : ISuspendAudit<TAccountId>
{
    /// <summary>Gets the navigation reference to the account that suspended this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount? SuspendedBy { get; }

    /// <summary>Gets the navigation reference to the account that unsuspended this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount? UnsuspendedBy { get; }

    /// <summary>Marks the entity as suspended, recording the timestamp and the responsible account.</summary>
    /// <param name="now">The UTC timestamp of the suspension.</param>
    /// <param name="byId">The identifier of the account performing the suspension, or <see langword="null"/> if unknown.</param>
    /// <param name="by">The navigation reference to the account performing the suspension, or <see langword="null"/> if not loaded.</param>
    void Suspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    /// <summary>Lifts the suspension on the entity, clearing suspension fields and recording the unsuspension timestamp and account.</summary>
    /// <param name="now">The UTC timestamp of the unsuspension.</param>
    /// <param name="byId">The identifier of the account performing the unsuspension, or <see langword="null"/> if unknown.</param>
    /// <param name="by">The navigation reference to the account performing the unsuspension, or <see langword="null"/> if not loaded.</param>
    void Unsuspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
