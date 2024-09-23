// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface ICreateAudit
{
    /// <summary>Timestamp when this entity was created.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset DateCreated { get; }
}

public interface ICreateAudit<out TAccountId> : ICreateAudit
{
    /// <summary>ID of the account who create this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? CreatedById { get; }
}

public interface ICreateAudit<out TAccountId, out TAccount> : ICreateAudit<TAccountId>
{
    /// <summary>ID of the account who last updated this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount CreatedBy { get; }
}
