// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity with suspension audit fields.</summary>
[PublicAPI]
public interface ISuspendAudit
{
    /// <summary>Indicates whether this entity is suspended.</summary>
    bool IsSuspended { get; }

    /// <summary>Date and time the entity was suspended. when it has a value it means that this entity is suspended.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateSuspended { get; }

    /// <summary>Date and time the entity was unsuspended.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateUnsuspended { get; }
}

/// <summary>Extends <c>ISuspendAudit</c> with the identifiers of the accounts that suspended and unsuspended the entity.</summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
[PublicAPI]
public interface ISuspendAudit<out TAccountId> : ISuspendAudit
{
    /// <summary>ID of the account that suspend this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? SuspendedById { get; }

    /// <summary>ID of the account that unsuspended this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? UnsuspendedById { get; }
}

/// <summary>
/// Extends <c>ISuspendAudit&lt;TAccountId&gt;</c> with navigation links to the accounts that suspended
/// and unsuspended the entity, and with methods to transition the entity between those states.
/// </summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
/// <typeparam name="TAccount">Type of the account entity.</typeparam>
[PublicAPI]
public interface ISuspendAudit<TAccountId, TAccount> : ISuspendAudit<TAccountId>
{
    /// <summary>Expandable link to the account who suspend this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? SuspendedBy { get; }

    /// <summary>Expandable link to the account who unsuspended this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? UnsuspendedBy { get; }

    /// <summary>Marks the entity as suspended, recording the timestamp and the account responsible.</summary>
    /// <param name="now">UTC timestamp of the suspend operation.</param>
    /// <param name="byId">Identifier of the account performing the suspension, or <see langword="null"/> if unknown.</param>
    /// <param name="by">Navigation reference to the account performing the suspension, or <see langword="null"/> if not loaded.</param>
    void Suspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    /// <summary>Lifts the suspension on the entity, clearing the suspension fields and recording the unsuspension timestamp and account.</summary>
    /// <param name="now">UTC timestamp of the unsuspend operation.</param>
    /// <param name="byId">Identifier of the account performing the unsuspension, or <see langword="null"/> if unknown.</param>
    /// <param name="by">Navigation reference to the account performing the unsuspension, or <see langword="null"/> if not loaded.</param>
    void Unsuspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
