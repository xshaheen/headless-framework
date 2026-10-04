// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines multi-tenant scoping for an entity.</summary>
[PublicAPI]
public interface IMultiTenant
{
    /// <summary>Gets the associated tenant identifier, or <see langword="null"/> for host-level entities.</summary>
    string? TenantId { get; }
}
