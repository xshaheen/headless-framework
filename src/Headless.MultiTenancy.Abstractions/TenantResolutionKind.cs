// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>The category of a <see cref="TenantResolutionOutcome"/>.</summary>
[PublicAPI]
public enum TenantResolutionKind
{
    /// <summary>
    /// Not a resolution result. Reserved as the zero value so an uninitialized
    /// <see cref="TenantResolutionOutcome"/> — a <see langword="default"/> struct, an auto-valued test
    /// double, or a consumer-supplied catalog service that returns one — never masquerades as
    /// <see cref="Resolved"/> while carrying a <see langword="null"/>
    /// <see cref="TenantResolutionOutcome.Tenant"/>. The catalog never produces this value; consumers
    /// should treat it as a contract violation.
    /// </summary>
    None = 0,

    /// <summary>The identifier resolved to an enabled tenant.</summary>
    Resolved = 1,

    /// <summary>The identifier has no matching catalog row.</summary>
    Unknown = 2,

    /// <summary>The identifier resolved to a disabled tenant.</summary>
    Disabled = 3,

    /// <summary>The identifier is on the ignored-identifiers list; the store was never consulted.</summary>
    Ignored = 4,

    /// <summary>The identifier failed shape validation before any cache or store lookup.</summary>
    Invalid = 5,
}
