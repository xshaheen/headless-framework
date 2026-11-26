// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Fencing;

/// <summary>
/// The position of a listed lease in the inspector's order: tenant (the host scope first), kind, then resource, each
/// compared ordinally.
/// </summary>
/// <param name="TenantId">The lease's tenant, or <see langword="null" /> for the host scope.</param>
/// <param name="Kind">The lease kind.</param>
/// <param name="Resource">The leased resource.</param>
[PublicAPI]
public sealed record FencedLeasePosition(string? TenantId, string Kind, string Resource)
{
    /// <summary>Returns the position of <paramref name="snapshot" />.</summary>
    /// <param name="snapshot">A listed lease.</param>
    /// <returns>Its position.</returns>
    public static FencedLeasePosition Of(LeaseSnapshot snapshot)
    {
        Argument.IsNotNull(snapshot);

        return new FencedLeasePosition(snapshot.Lease.TenantId, snapshot.Lease.Kind, snapshot.Lease.Resource);
    }
}
