// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Permission claim values the Jobs dashboard evaluates when <see cref="DashboardOptionsBuilder.WithHostAuthentication"/>
/// is used. The host application issues them on <c>HttpContext.User</c> under the claim type configured by
/// <see cref="DashboardOptionsBuilder.WithPermissionClaimType"/> (default <c>permission</c>).
/// </summary>
/// <remarks>
/// <see cref="Admin"/> implies <see cref="Read"/>; a caller never needs both. The single-credential Basic, API-key,
/// and custom modes cannot express per-user permissions, so a successfully authenticated caller in those modes — and
/// every caller when <see cref="DashboardOptionsBuilder.WithNoAuth"/> is selected — is treated as
/// <see cref="Admin"/>.
/// </remarks>
[PublicAPI]
public static class JobsDashboardPermissions
{
    /// <summary>Permits dashboard reads and live-notification hub subscriptions.</summary>
    public const string Read = "headless.jobs.read";

    /// <summary>Permits every read and every mutation, including host control and cross-tenant time-job changes.</summary>
    public const string Admin = "headless.jobs.admin";
}
