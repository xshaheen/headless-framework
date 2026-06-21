// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity as belonging to a specific tenant in a multi-tenant system.</summary>
[PublicAPI]
public interface IMultiTenant
{
    /// <summary>ID of the related tenant.</summary>
    string? TenantId { get; }
}
