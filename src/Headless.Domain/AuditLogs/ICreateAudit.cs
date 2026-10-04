// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines creation audit fields for an entity.</summary>
[PublicAPI]
public interface ICreateAudit
{
    /// <summary>Gets the timestamp when this entity was created.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    DateTimeOffset CreatedAt { get; }
}

/// <summary>Defines creation audit fields that include the creator account identifier.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public interface ICreateAudit<out TAccountId> : ICreateAudit
{
    /// <summary>Gets the identifier of the account that created this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? CreatedById { get; }
}

/// <summary>Defines creation audit fields that include a navigation reference to the creator account.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public interface ICreateAudit<out TAccountId, out TAccount> : ICreateAudit<TAccountId>
{
    /// <summary>Gets the navigation reference to the account that created this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount CreatedBy { get; }
}
