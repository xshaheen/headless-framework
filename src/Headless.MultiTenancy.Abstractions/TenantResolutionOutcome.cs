// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// The closed set of outcomes produced by identifier-based tenant resolution (normalize →
/// shape-validate → ignored-check → cache/store lookup). Exactly one <see cref="Kind"/> applies per
/// resolution; <see cref="Tenant"/> is populated only for <see cref="TenantResolutionKind.Resolved"/>.
/// </summary>
/// <remarks>
/// A value type so the common resolution path completes without a heap allocation, mirroring the
/// caching family's <c>CacheValue&lt;T&gt;</c> read-result envelope shape.
/// </remarks>
[PublicAPI]
public readonly struct TenantResolutionOutcome : IEquatable<TenantResolutionOutcome>
{
    private TenantResolutionOutcome(TenantResolutionKind kind, TenantInfo? tenant)
    {
        Kind = kind;
        Tenant = tenant;
    }

    /// <summary>The resolution outcome category.</summary>
    public TenantResolutionKind Kind { get; }

    /// <summary>The resolved, enabled tenant. Non-null only when <see cref="Kind"/> is <see cref="TenantResolutionKind.Resolved"/>.</summary>
    public TenantInfo? Tenant { get; }

    /// <summary>An outcome for an identifier that resolved to an enabled tenant.</summary>
    /// <param name="tenant">The resolved, enabled tenant.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tenant"/> is <see langword="null"/>.</exception>
    public static TenantResolutionOutcome Resolved(TenantInfo tenant)
    {
        Argument.IsNotNull(tenant);

        return new TenantResolutionOutcome(TenantResolutionKind.Resolved, tenant);
    }

    /// <summary>An outcome for an identifier with no matching catalog row.</summary>
    public static TenantResolutionOutcome Unknown { get; } = new(TenantResolutionKind.Unknown, tenant: null);

    /// <summary>An outcome for an identifier that resolved to a disabled tenant.</summary>
    public static TenantResolutionOutcome Disabled { get; } = new(TenantResolutionKind.Disabled, tenant: null);

    /// <summary>An outcome for an identifier on the ignored-identifiers list; the store was never consulted.</summary>
    public static TenantResolutionOutcome Ignored { get; } = new(TenantResolutionKind.Ignored, tenant: null);

    /// <summary>An outcome for an identifier that failed shape validation before any cache or store lookup.</summary>
    public static TenantResolutionOutcome Invalid { get; } = new(TenantResolutionKind.Invalid, tenant: null);

    /// <summary>Determines whether this outcome has the same <see cref="Kind"/> and <see cref="Tenant"/> reference as <paramref name="other"/>.</summary>
    /// <param name="other">The outcome to compare against.</param>
    public bool Equals(TenantResolutionOutcome other)
    {
        return Kind == other.Kind && ReferenceEquals(Tenant, other.Tenant);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is TenantResolutionOutcome other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, Tenant);
    }

    /// <summary>Determines whether two outcomes have the same <see cref="Kind"/> and <see cref="Tenant"/> reference.</summary>
    public static bool operator ==(TenantResolutionOutcome left, TenantResolutionOutcome right) => left.Equals(right);

    /// <summary>Determines whether two outcomes differ in <see cref="Kind"/> or <see cref="Tenant"/> reference.</summary>
    public static bool operator !=(TenantResolutionOutcome left, TenantResolutionOutcome right) => !left.Equals(right);
}

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
