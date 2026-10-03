// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>Resolves the <see cref="ScopedCache{T}"/> scope for a tenant-scoped cache.</summary>
internal static class TenantCacheScope
{
    /// <summary>The scope prefix; the same layout the permission grant cache uses.</summary>
    private const string _ScopePrefix = "t:";

    public static string Resolve(ICurrentTenant currentTenant)
    {
        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                "A tenant-scoped cache was used with no ambient tenant. Wrap the call in "
                    + "ICurrentTenant.Change(tenantId), or cache values every tenant shares through the unscoped "
                    + "ICache."
            );
        }

        if (tenantId.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A tenant-scoped cache cannot scope a tenant id that contains ':', because the resulting key would "
                    + "be indistinguishable from another tenant's key."
            );
        }

        return _ScopePrefix + tenantId;
    }
}
