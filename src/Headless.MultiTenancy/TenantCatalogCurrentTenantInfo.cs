// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// <see cref="ICurrentTenantInfo"/> implementation backed by the catalog service: resolves
/// against <see cref="ICurrentTenant.Id"/> observed at each call — no per-scope memoization — so nested
/// <see cref="ICurrentTenant.Change"/> scopes always see the inner tenant's info while active and the
/// outer tenant's info again once the scope disposes.
/// </summary>
internal sealed class TenantCatalogCurrentTenantInfo(ICurrentTenant currentTenant, ITenantCatalogService catalogService)
    : ICurrentTenantInfo
{
    public Task<TenantInfo?> GetAsync(CancellationToken cancellationToken = default)
    {
        var id = currentTenant.Id;

        return id is null ? Task.FromResult<TenantInfo?>(null) : catalogService.FindByIdAsync(id, cancellationToken);
    }
}
