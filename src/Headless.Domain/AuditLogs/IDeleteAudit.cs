// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity with soft-delete audit fields.</summary>
[PublicAPI]
public interface IDeleteAudit
{
    /// <summary>Indicates whether this entity is soft-deleted.</summary>
    bool IsDeleted { get; }

    /// <summary>Date and time the entity was soft-deleted. when it has a value it means that this entity is soft-deleted.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateDeleted { get; }

    /// <summary>Date and time the entity was restored.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateRestored { get; }
}

/// <summary>Extends <c>IDeleteAudit</c> with the identifiers of the accounts that soft-deleted and restored the entity.</summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
[PublicAPI]
public interface IDeleteAudit<out TAccountId> : IDeleteAudit
{
    /// <summary>ID of the account that delete this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? DeletedById { get; }

    /// <summary>ID of the account that restore this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? RestoredById { get; }
}

/// <summary>
/// Extends <c>IDeleteAudit&lt;TAccountId&gt;</c> with navigation links to the accounts that soft-deleted
/// and restored the entity, and with methods to transition the entity between those states.
/// </summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
/// <typeparam name="TAccount">Type of the account entity.</typeparam>
[PublicAPI]
public interface IDeleteAudit<TAccountId, TAccount> : IDeleteAudit<TAccountId>
{
    /// <summary>Expandable link to the account who deleted this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? DeletedBy { get; }

    /// <summary>Expandable link to the account who restore this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? RestoredBy { get; }

    /// <summary>Marks the entity as soft-deleted, recording the timestamp and the account responsible.</summary>
    /// <param name="now">UTC timestamp of the delete operation.</param>
    /// <param name="byId">Identifier of the account performing the delete, or <see langword="null"/> if unknown.</param>
    /// <param name="by">Navigation reference to the account performing the delete, or <see langword="null"/> if not loaded.</param>
    void Delete(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    /// <summary>Restores a soft-deleted entity, clearing the delete fields and recording the restoration timestamp and account.</summary>
    /// <param name="now">UTC timestamp of the restore operation.</param>
    /// <param name="byId">Identifier of the account performing the restore, or <see langword="null"/> if unknown.</param>
    /// <param name="by">Navigation reference to the account performing the restore, or <see langword="null"/> if not loaded.</param>
    void Restore(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
