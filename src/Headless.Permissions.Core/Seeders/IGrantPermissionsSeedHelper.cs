// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Repositories;

namespace Headless.Permissions.Seeders;

/// <summary>
/// Seed-time helper for granting permissions during data initialization. Intended for use inside
/// data seeders or hosted startup services, not in the application request path.
/// </summary>
public interface IGrantPermissionsSeedHelper
{
    /// <summary>
    /// Grants every currently-defined permission that allows the <c>Role</c> provider to the given role,
    /// skipping any permission that already has a grant record (idempotent). Runs under
    /// <paramref name="tenantId"/> when provided; otherwise runs under the ambient tenant.
    /// </summary>
    /// <param name="roleName">Name of the role to receive the grants.</param>
    /// <param name="tenantId">Optional tenant to scope the grants; uses the ambient tenant when <see langword="null"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="roleName"/> or <paramref name="tenantId"/> is text some provider would not keep unchanged as a key.</exception>
    ValueTask GrantAllPermissionsToRoleAsync(
        string roleName,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );
}
