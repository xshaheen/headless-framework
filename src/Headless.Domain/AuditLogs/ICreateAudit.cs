// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

[PublicAPI]
public interface ICreateAudit
{
    /// <summary>Timestamp when this entity was created.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset DateCreated { get; }
}

[PublicAPI]
public interface ICreateAudit<out TAccountId> : ICreateAudit
{
    /// <summary>ID of the account who created this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? CreatedById { get; }
}

[PublicAPI]
public interface ICreateAudit<out TAccountId, out TAccount> : ICreateAudit<TAccountId>
{
    /// <summary>Navigation link to the account who created this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount CreatedBy { get; }
}
