// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Default <see cref="ICurrentTenantInfo"/> registered when no catalog store is configured:
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
