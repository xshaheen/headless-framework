// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>
/// The default <see cref="ITenantDataPlacementResolver"/>: every tenant has no placement. Hosts that never route a
/// data context never consult it; a host that routes one without configuring a placement source fails startup.
/// </summary>
internal sealed class NullTenantDataPlacementResolver : ITenantDataPlacementResolver
{
    public Task<TenantDataPlacement?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<TenantDataPlacement?>(null);
    }
}
