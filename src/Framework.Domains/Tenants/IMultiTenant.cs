// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domains;

public interface IMultiTenant
{
    /// <summary>ID of the related tenant.</summary>
    string? TenantId { get; }
}
