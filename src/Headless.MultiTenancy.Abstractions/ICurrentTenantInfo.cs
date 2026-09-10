// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Reads catalog <see cref="TenantInfo"/> for the ambient tenant. Implementations resolve by reading
/// <see cref="ICurrentTenant.Id"/> at each call — there is no per-scope memoization — so nested
/// <see cref="ICurrentTenant.Change"/> scopes (Jobs retry, Messaging consume, admin flows) always
/// observe the inner tenant's info once the scope activates, and the outer tenant's info again once
/// it disposes.
/// </summary>
/// <remarks>
/// Reads never throw for an absent tenant: they return <see langword="null"/> when no tenant context
/// is ambient, no catalog store is configured, or the ambient id has no matching catalog row. A
/// disabled tenant's metadata still reads (<see cref="TenantInfo.IsEnabled"/> is <see langword="false"/>)
/// — rejecting a disabled tenant is a resolution-time concern only, never an accessor concern. When the
/// ambient display name and the catalog name differ (for example after a Jobs/Messaging-restored
/// scope), the value returned here is authoritative.
/// </remarks>
[PublicAPI]
public interface ICurrentTenantInfo
{
    /// <summary>Loads <see cref="TenantInfo"/> for the ambient tenant.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The ambient tenant's <see cref="TenantInfo"/>, or <see langword="null"/> when no tenant context
    /// is ambient, no catalog store is configured, or the ambient id has no matching catalog row.
    /// </returns>
    Task<TenantInfo?> GetAsync(CancellationToken cancellationToken = default);
}
