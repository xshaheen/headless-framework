// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Identifies a tenant by its id and optional name. A <see langword="null"/> <paramref name="tenantId"/> indicates
/// the host (no tenant) context.
/// </summary>
/// <param name="tenantId">The tenant identifier; <see langword="null"/> indicates the host.</param>
/// <param name="name">The optional tenant name.</param>
public sealed class TenantInformation(string? tenantId, string? name = null)
{
    /// <summary>Null indicates the host. Not null value for a tenant.</summary>
    public string? TenantId { get; } = tenantId;

    /// <summary>Name of the tenant if <see cref="TenantId"/> is not null.</summary>
    public string? Name { get; } = name;
}
