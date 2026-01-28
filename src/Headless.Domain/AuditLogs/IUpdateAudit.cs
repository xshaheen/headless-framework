// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

public interface IUpdateAudit
{
    /// <summary>Timestamp when this entity was last updated.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateUpdated { get; }
}

public interface IUpdateAudit<out TAccountId> : IUpdateAudit
{
    /// <summary>ID of the account who last updated this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? UpdatedById { get; }
}

public interface IUpdateAudit<out TAccountId, out TAccount> : IUpdateAudit<TAccountId>
{
    /// <summary>ID of the account who last updated this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? UpdatedBy { get; }
}
