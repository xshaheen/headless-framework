// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

public sealed class TenantInformation(string? tenantId, string? name = null)
{
    /// <summary>Null indicates the host. Not null value for a tenant.</summary>
    public string? TenantId { get; } = tenantId;

    /// <summary>Name of the tenant if <see cref="TenantId"/> is not null.</summary>
    public string? Name { get; } = name;
}
