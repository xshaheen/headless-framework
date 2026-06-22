// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity with creation-time audit fields.</summary>
[PublicAPI]
public interface ICreateAudit
{
    /// <summary>Timestamp when this entity was created.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset DateCreated { get; }
}

/// <summary>Extends <c>ICreateAudit</c> with the identifier of the account that created the entity.</summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
[PublicAPI]
public interface ICreateAudit<out TAccountId> : ICreateAudit
{
    /// <summary>ID of the account who created this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? CreatedById { get; }
}

/// <summary>Extends <c>ICreateAudit&lt;TAccountId&gt;</c> with a navigation link to the creating account entity.</summary>
/// <typeparam name="TAccountId">Type of the account identifier.</typeparam>
/// <typeparam name="TAccount">Type of the account entity.</typeparam>
[PublicAPI]
public interface ICreateAudit<out TAccountId, out TAccount> : ICreateAudit<TAccountId>
{
    /// <summary>Navigation link to the account who created this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount CreatedBy { get; }
}
