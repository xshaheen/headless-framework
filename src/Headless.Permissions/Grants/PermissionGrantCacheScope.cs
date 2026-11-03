// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Headless.Permissions;

/// <summary>Builds the tenant scope of the permission grant cache: <c>t:{tenantId}</c>, empty id for the host.</summary>
/// <remarks>
/// The scope is read for every cache key, and a permission check reaches the cache with one key per permission and
/// role. Keeping the last tenant's scope string lets every key of a check, and every check of the same tenant, reuse
/// it instead of formatting it again. The last scope is held in one immutable reference, so concurrent requests of
/// different tenants only replace it; none can read a scope paired with another tenant's id.
/// </remarks>
internal sealed class PermissionGrantCacheScope(ICurrentTenant currentTenant)
{
    private Entry? _last;

    public string Get()
    {
        var tenantId = currentTenant.Id;
        var last = Volatile.Read(ref _last);

        if (last is not null && string.Equals(last.TenantId, tenantId, StringComparison.Ordinal))
        {
            return last.Scope;
        }

        var scope = $"t:{tenantId}";
        Volatile.Write(ref _last, new Entry(tenantId, scope));

        return scope;
    }

    private sealed record Entry(string? TenantId, string Scope);
}
