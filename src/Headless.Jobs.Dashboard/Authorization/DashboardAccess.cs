// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;

namespace Headless.Jobs.Authorization;

/// <summary>
/// Access class of a dashboard route. Every mapped route carries exactly one <see cref="DashboardAccessMetadata"/>;
/// the group endpoint filter and the notification hub read it so classification, enforcement, and tests share one
/// source of truth.
/// </summary>
internal enum DashboardAccess
{
    /// <summary>Authentication bootstrap routes reachable without credentials.</summary>
    Anonymous = 0,

    /// <summary>Requires <see cref="JobsDashboardPermissions.Read"/> (or admin).</summary>
    Read = 1,

    /// <summary>
    /// Requires read permission to reach the handler; the handler then authorizes against the persisted row tenant
    /// (same tenant, or admin).
    /// </summary>
    TenantRowMutation = 2,

    /// <summary>Requires <see cref="JobsDashboardPermissions.Admin"/>.</summary>
    Admin = 3,
}

internal sealed record DashboardAccessMetadata(DashboardAccess Access);

internal static class DashboardAccessConventions
{
    internal static TBuilder WithAccess<TBuilder>(this TBuilder builder, DashboardAccess access)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.WithMetadata(new DashboardAccessMetadata(access));
    }
}
