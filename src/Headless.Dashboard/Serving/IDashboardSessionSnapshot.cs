// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard;

/// <summary>
/// Reads the current request's dashboard session, so the shell document inlines the signed-in state and the modules
/// the user may see, and the first render needs no API round trip.
/// </summary>
internal interface IDashboardSessionSnapshot
{
    /// <summary>Returns the session of <paramref name="context"/>'s user.</summary>
    /// <param name="context">The request whose session is read.</param>
    /// <param name="cancellationToken">Cancels the read when the request is aborted.</param>
    /// <returns>The session, with no modules when the request carries none.</returns>
    ValueTask<DashboardSessionSnapshot> GetAsync(HttpContext context, CancellationToken cancellationToken);
}

/// <summary>Reads the session through <see cref="DashboardPermissions"/>, the same answer <c>api/auth/info</c> gives.</summary>
internal sealed class PermissionsDashboardSessionSnapshot(DashboardPermissions permissions) : IDashboardSessionSnapshot
{
    public async ValueTask<DashboardSessionSnapshot> GetAsync(HttpContext context, CancellationToken cancellationToken)
    {
        return await permissions.GetSessionSnapshotAsync(context).ConfigureAwait(false);
    }
}
