// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.MultiTenancy;

/// <summary>
/// A single tenant seed as bound from configuration. A plain, publicly settable shape — rather than
/// binding <see cref="TenantInfo"/> directly — because <see cref="TenantInfo"/> exposes no
/// parameterless constructor for the options binder to target. <see cref="ConfigurationTenantStore"/>
/// converts each bound seed into a <see cref="TenantInfo"/> through its normal validating constructor —
/// the domain type itself is never constructed through uninitialized-object reflection.
/// </summary>
[PublicAPI]
public sealed class ConfigurationTenantSeed
{
    /// <summary>The canonical tenant id. See <see cref="TenantInfo.Id"/>.</summary>
    public string Id { get; set; } = "";

    /// <summary>The public-facing tenant identifier. See <see cref="TenantInfo.Identifier"/>.</summary>
    public string Identifier { get; set; } = "";

    /// <summary>The tenant's display name, or <see langword="null"/> when not set.</summary>
    public string? Name { get; set; }

    /// <summary>Whether the tenant is enabled. Default: <see langword="true"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Read-along extra properties, bound from nested configuration keys under this seed's
    /// <c>ExtraProperties</c> section (for example <c>Tenants:0:ExtraProperties:Region</c>).
    /// Configuration leaf values are always strings; each entry is copied as-is into the resulting
    /// <see cref="Primitives.ExtraProperties"/> bag.
    /// </summary>
    public IDictionary<string, string> ExtraProperties { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
