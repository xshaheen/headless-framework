// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

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

public interface IDeleteAudit<out TAccountId> : IDeleteAudit
{
    /// <summary>ID of the account that delete this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? DeletedById { get; }

    /// <summary>ID of the account that restore this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? RestoredById { get; }
}

public interface IDeleteAudit<TAccountId, TAccount> : IDeleteAudit<TAccountId>
{
    /// <summary>Expandable link to the account who deleted this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? DeletedBy { get; }

    /// <summary>Expandable link to the account who restore this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? RestoredBy { get; }

    void Delete(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    void Restore(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
