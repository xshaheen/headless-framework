// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Marks an entity as belonging to a specific tenant in a multi-tenant system.</summary>
[PublicAPI]
public interface IMultiTenant
{
    /// <summary>ID of the related tenant.</summary>
    string? TenantId { get; }
}
