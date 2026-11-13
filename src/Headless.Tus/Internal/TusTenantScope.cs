// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Tus.Internal;

/// <summary>
/// Tells an upload store which tenant the uploads of the current call belong to, so the store can keep each
/// tenant's uploads under a path segment of their own. A store asks on every call and keeps no answer: the store is
/// one instance for the host, and the tenant is the caller's.
/// </summary>
/// <remarks>
/// It reads the ambient tenant, so it serves the paths that run for one caller. A pass that covers every tenant,
/// such as the removal of expired uploads, runs with no ambient tenant and names each tenant itself.
/// </remarks>
internal sealed class TusTenantScope(IOptions<TusTenantScopingOptions> options, IServiceProvider services)
{
    private ICurrentTenant? _currentTenant;

    /// <summary>
    /// The path segment the current call's uploads are kept under, or <see langword="null"/> when uploads are not
    /// scoped by tenant.
    /// </summary>
    /// <exception cref="MissingTenantContextException">Uploads are scoped by tenant and no tenant is ambient.</exception>
    /// <exception cref="InvalidOperationException">The ambient tenant id cannot be used as a storage path segment.</exception>
    public string? GetSegment()
    {
        if (!options.Value.Enabled)
        {
            return null;
        }

        // Resolved on first use, and only once uploads are scoped: a host that never scopes them may have no
        // current tenant registered, or one this singleton could not hold. The tenancy setup that turns scoping on
        // registers one.
        var tenantId = (_currentTenant ??= services.GetRequiredService<ICurrentTenant>()).Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                "A tenant-scoped upload store was used with no ambient tenant. Wrap the call in "
                    + "ICurrentTenant.Change(tenantId) with the tenant the upload belongs to."
            );
        }

        // Checked, never encoded: an id rewritten to fit could become another tenant's segment.
        if (!TenantStorageSegment.IsValid(tenantId))
        {
            throw new InvalidOperationException(
                "A tenant-scoped upload store cannot use this tenant id as a path segment. A tenant id must start "
                    + "and end with a lowercase ASCII letter or digit and contain only lowercase ASCII letters, "
                    + "digits, '.', '_', and '-'."
            );
        }

        return tenantId;
    }
}
