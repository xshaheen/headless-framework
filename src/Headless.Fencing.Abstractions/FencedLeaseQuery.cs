// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>The leases an <see cref="IFencedLeaseInspector" /> list or count reads.</summary>
/// <remarks>Every filter that is set must match. This record grows by <see langword="init" /> members.</remarks>
[PublicAPI]
public sealed record FencedLeaseQuery
{
    /// <summary>Gets the tenant whose leases are read; <see cref="LeaseTenantFilter.Any" /> by default.</summary>
    public LeaseTenantFilter Tenant { get; init; }

    /// <summary>Gets the one lease kind read, compared ordinally, or <see langword="null" /> for every kind.</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Gets the prefix every listed resource starts with, compared ordinally (case-sensitive), or
    /// <see langword="null" /> for every resource.
    /// </summary>
    /// <remarks>
    /// The match is literal: <c>%</c>, <c>_</c>, <c>[</c>, <c>*</c>, and <c>?</c> are ordinary characters, never wildcards.
    /// </remarks>
    public string? ResourcePrefix { get; init; }

    /// <summary>Gets the state the leases must be in; <see cref="FencedLeaseStateFilter.All" /> by default.</summary>
    public FencedLeaseStateFilter State { get; init; }
}
