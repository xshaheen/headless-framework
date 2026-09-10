// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Default <see cref="ICurrentTenantInfo"/> registered when no catalog store is configured (R5/R9):
/// every read returns <see langword="null"/>, matching today's behavior for hosts that never opt into
/// the catalog. <c>Catalog(...)</c> replaces this registration with <see cref="TenantCatalogCurrentTenantInfo"/>.
/// </summary>
internal sealed class NullCurrentTenantInfo : ICurrentTenantInfo
{
    public Task<TenantInfo?> GetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TenantInfo?>(null);
    }
}

/// <summary>
/// <see cref="ICurrentTenantInfo"/> implementation backed by the catalog service (KTD3): resolves
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
