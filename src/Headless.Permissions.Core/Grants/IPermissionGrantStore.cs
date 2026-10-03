// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Grants;

/// <summary>
/// Caching read/write layer over the permission grant repository. Reads are served from a
/// tenant-scoped cache (<see cref="PermissionManagementOptions.GrantCacheExpiration"/>, 5 hours by default) and
/// populated on first miss by loading all grants for the provider in a single repository call. Writes update both
/// the repository and the cache atomically.
/// <para>
/// An explicit <c>Revoke</c> writes a denial record (<c>IsGranted = false</c>) — it does NOT delete
/// the row — so the cache can distinguish <c>Prohibited</c> from <c>Undefined</c>.
/// </para>
/// <para>
/// Every member throws <see cref="ArgumentException"/> before touching storage when a permission name, provider name,
/// provider key, or tenant id is text some provider would not keep unchanged: surrounding white space (SQL Server ignores trailing spaces when
/// it compares keys, so <c>"acme"</c> and <c>"acme "</c> would address one row there and two on PostgreSQL), a NUL
/// character (PostgreSQL cannot store it), or an unpaired UTF-16 surrogate (SqlClient rewrites it to U+FFFD, merging
/// distinct keys). See <c>Argument.IsPortableKey</c>.
/// </para>
/// </summary>
public interface IPermissionGrantStore
{
    /// <summary>
    /// Returns the grant status of a single permission for the given provider target. A cache miss triggers
    /// a full load of all grants for that provider target so subsequent lookups are served from cache.
    /// </summary>
    Task<PermissionGrantStatus> IsGrantedAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the grant status for each of the requested permission names for the given provider target.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
    Task<Dictionary<string, PermissionGrantStatus>> IsGrantedAsync(
        IReadOnlyList<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all grant records (granted and denied) for the given provider target.</summary>
    Task<List<PermissionGrant>> GetAllGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants a permission for the provider target. If a denial record already exists it is replaced with
    /// a grant; if the record is already granted the call is a no-op. Updates the cache on success.
    /// </summary>
    Task GrantAsync(
        string name,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch overload of <see cref="GrantAsync(string, string, string, string?, CancellationToken)"/>.
    /// Skips names that already have an active grant record. Converts denial records to grants.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
    Task GrantAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes an explicit denial (Prohibited) for the provider target. If a grant record already exists it
    /// is replaced with a denial; if the record is already denied the call is a no-op. Updates the cache.
    /// </summary>
    Task RevokeAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch overload of <see cref="RevokeAsync(string, string, string, CancellationToken)"/>.
    /// Converts grant records to denials and inserts denial records for names with no existing record.
    /// </summary>
    /// <param name="names">Must not be empty.</param>
    Task RevokeAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reloads every grant for the provider target from the repository and replaces the cached statuses, so a
    /// long-lived consumer can observe a change made outside <see cref="IPermissionManager"/> and the repository
    /// before <see cref="PermissionManagementOptions.GrantCacheExpiration"/> elapses.
    /// </summary>
    Task RefreshAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
