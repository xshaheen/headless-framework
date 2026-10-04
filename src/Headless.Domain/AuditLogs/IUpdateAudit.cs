// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines last-update audit fields for an entity.</summary>
[PublicAPI]
public interface IUpdateAudit
{
    /// <summary>Gets the timestamp when this entity was last updated.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    DateTimeOffset? UpdatedAt { get; }
}

/// <summary>Defines last-update audit fields that include the modifying account identifier.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public interface IUpdateAudit<out TAccountId> : IUpdateAudit
{
    /// <summary>Gets the identifier of the account that last updated this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? UpdatedById { get; }
}

/// <summary>Defines last-update audit fields that include a navigation reference to the modifying account.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public interface IUpdateAudit<out TAccountId, out TAccount> : IUpdateAudit<TAccountId>
{
    /// <summary>Gets the navigation reference to the account that last updated this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount? UpdatedBy { get; }
}
