// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Optional enumeration capability for an <see cref="ITenantStore"/>. A store implements this
/// interface alongside <see cref="ITenantStore"/> when it can list its tenants. All v1 stores shipped
/// by this framework implement it; the catalog service itself never calls this member — enumeration
/// exists for app-owned fan-out (for example, a cron job that iterates every tenant), not for
/// framework-built features.
/// </summary>
[PublicAPI]
public interface ITenantDirectory
{
    /// <summary>Returns every tenant known to the store, including disabled tenants.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>All tenants known to the store, unpaged. The list may be empty but is never <see langword="null"/>.</returns>
    Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken cancellationToken = default);
}
