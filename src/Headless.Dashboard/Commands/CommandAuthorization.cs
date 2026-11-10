// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>Decides whether the signed-in user may run one command, and says why not when they may not.</summary>
/// <remarks>
/// <para>
/// A command needs its module's view policy, which the command endpoints' route group already enforces, and then the
/// permission it declares:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="DashboardCommandPermission.View"/>: nothing more.</description></item>
///   <item><description>
///   A named <see cref="DashboardCommand.Capability"/> the host has mapped by registering an authorization policy of
///   that name: that policy, instead of the module's operate policy.
///   </description></item>
///   <item><description>Otherwise, the module's operate policy.</description></item>
/// </list>
/// <para>
/// A call the module's <see cref="IDashboardPeerAccess"/> scheme admitted passes every command check: the module's peer
/// policy authorized it, and the node that forwarded it checked the permission for its own user.
/// </para>
/// <para>
/// Policies apply under host authentication only. Basic and API-key authentication share one credential for every
/// module, so an authenticated user may run every command, and no authentication opens every command to everyone.
/// </para>
/// </remarks>
internal static class CommandAuthorization
{
    /// <summary>The disabled reason of a command that needs the module's operate policy.</summary>
    public const string NeedsOperate = "Needs operate permission";

    /// <summary>The disabled reason of a command whose module the user may not view.</summary>
    public const string NeedsView = "Needs view permission";

    /// <summary>
    /// Returns why the request's user may not run <paramref name="command"/> of the module <paramref name="moduleId"/>,
    /// or <see langword="null"/> when they may.
    /// </summary>
    /// <exception cref="InvalidOperationException">A module or the shell names a policy the host does not register.</exception>
    public static async Task<string?> GetRefusalAsync(HttpContext context, string moduleId, DashboardCommand command)
    {
        // A peer call passed the module's peer policy instead; the node that forwarded it checked its own user.
        if (DashboardPeerAdmission.IsAdmitted(context, moduleId))
        {
            return null;
        }

        var services = context.RequestServices;
        var permissions = services.GetRequiredService<DashboardPermissions>();
        var mode = services
            .GetRequiredService<IOptionsMonitor<AuthConfig>>()
            .Get(SetupDashboard.AuthenticationName)
            .Mode;
        var user = context.User;
        var operate = command.Permission == DashboardCommandPermission.Operate;

        if (mode != AuthMode.Host)
        {
            var allowed = await permissions
                .CanAccessAsync(context, user, session: null, moduleId, operate)
                .ConfigureAwait(false);

            return allowed ? null
                : operate ? NeedsOperate
                : NeedsView;
        }

        var session = (
            await DashboardHostSession
                .AuthenticateAsync(context, SetupDashboard.AuthenticationName)
                .ConfigureAwait(false)
        ).Properties;

        if (!await permissions.CanAccessAsync(context, user, session, moduleId, operate: false).ConfigureAwait(false))
        {
            return NeedsView;
        }

        if (!operate)
        {
            return null;
        }

        if (await _MappedCapabilityAsync(services, command).ConfigureAwait(false) is { } capability)
        {
            return await DashboardHostAuthorization
                .PassesHostPolicyAsync(context, user, session, capability)
                .ConfigureAwait(false)
                ? null
                : $"Needs the {capability} permission";
        }

        return await permissions.CanAccessAsync(context, user, session, moduleId, operate: true).ConfigureAwait(false)
            ? null
            : NeedsOperate;
    }

    /// <summary>
    /// Returns the operator the request's user is recorded as: the name-identifier claim, else the name, else
    /// <c>anonymous</c>.
    /// </summary>
    public static string GetActor(ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(id))
        {
            return id;
        }

        return string.IsNullOrEmpty(user.Identity?.Name) ? "anonymous" : user.Identity.Name;
    }

    // A capability the host has not mapped to a policy falls back to the module's operate policy, so declaring one
    // never opens a command wider than operate.
    private static async Task<string?> _MappedCapabilityAsync(IServiceProvider services, DashboardCommand command)
    {
        if (string.IsNullOrEmpty(command.Capability))
        {
            return null;
        }

        var policies = services.GetRequiredService<IAuthorizationPolicyProvider>();

        return await policies.GetPolicyAsync(command.Capability).ConfigureAwait(false) is null
            ? null
            : command.Capability;
    }
}
