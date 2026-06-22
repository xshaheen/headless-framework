// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity with last-update audit fields.</summary>
[PublicAPI]
public interface IUpdateAudit
{
    /// <summary>Timestamp when this entity was last updated.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateUpdated { get; }
}

/// <summary>Extends <c>IUpdateAudit</c> with the identifier of the account that last updated the entity.</summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
[PublicAPI]
public interface IUpdateAudit<out TAccountId> : IUpdateAudit
{
    /// <summary>ID of the account who last updated this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? UpdatedById { get; }
}

/// <summary>Extends <c>IUpdateAudit&lt;TAccountId&gt;</c> with a navigation link to the account that last updated the entity.</summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
/// <typeparam name="TAccount">Type of the account entity.</typeparam>
[PublicAPI]
public interface IUpdateAudit<out TAccountId, out TAccount> : IUpdateAudit<TAccountId>
{
    /// <summary>Navigation link to the account who last updated this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? UpdatedBy { get; }
}
