// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Decides what a user may see and do in the dashboard shell: which modules they may view and operate, whether they
/// may hold a session at all, and the session state the shell reports to its page.
/// </summary>
/// <remarks>
/// <para>
/// Under <see cref="AuthMode.Host"/>, each module's view and operate policies (<see cref="DashboardModuleCatalog.GetPolicy"/>)
/// are evaluated against the session's copied claims; a <see langword="null"/> policy is the host's default policy. A
/// module may be operated only by a user who may also view it. Basic and API-key authentication share one credential
/// for every module, so an authenticated user may view and operate everything, and <see cref="AuthMode.None"/> opens
/// everything to everyone.
/// </para>
/// <para>
/// One evaluation runs each distinct policy once, so modules that inherit the shell's policies cost one check.
/// </para>
/// </remarks>
internal sealed class DashboardPermissions(DashboardModuleCatalog catalog, IOptionsMonitor<AuthConfig> auth)
{
    /// <summary>How a refused sign-in names the host's default policy, which has no name of its own.</summary>
    internal const string DefaultPolicyLabel = "the host's default policy";

    private const string _Name = SetupDashboard.AuthenticationName;

    /// <summary>
    /// Returns, for every registered module, whether <paramref name="user"/> may view and operate it.
    /// </summary>
    /// <param name="context">The request the policies are evaluated for.</param>
    /// <param name="user">The session's principal.</param>
    /// <param name="session">The session ticket's properties, which name the host schemes it was issued from.</param>
    /// <exception cref="InvalidOperationException">A module or the shell names a policy the host does not register.</exception>
    public async Task<IReadOnlyDictionary<string, DashboardModuleAccess>> GetModuleAccessAsync(
        HttpContext context,
        ClaimsPrincipal user,
        AuthenticationProperties? session
    )
    {
        var config = auth.Get(_Name);
        var access = new Dictionary<string, DashboardModuleAccess>(catalog.ModuleIds.Count, StringComparer.Ordinal);

        if (config.Mode != AuthMode.Host)
        {
            var open = config.Mode == AuthMode.None || _IsAuthenticated(user);
            foreach (var id in catalog.ModuleIds)
            {
                access[id] = new DashboardModuleAccess { CanView = open, CanOperate = open };
            }

            return access;
        }

        var passes = _SessionEvaluator(context, user, session);
        foreach (var id in catalog.ModuleIds)
        {
            access[id] = await _ModuleAccessAsync(id, passes).ConfigureAwait(false);
        }

        return access;
    }

    /// <summary>
    /// Whether <paramref name="user"/> may reach a shell endpoint: a module's endpoint when <paramref name="moduleId"/>
    /// names one, else one of the shell's own, which any user who may view the shell or one module may reach.
    /// </summary>
    /// <param name="context">The request the policies are evaluated for.</param>
    /// <param name="user">The session's principal.</param>
    /// <param name="session">The session ticket's properties.</param>
    /// <param name="moduleId">The module the endpoint belongs to, or <see langword="null"/> for the shell's own.</param>
    /// <param name="operate">Whether the endpoint changes state, so it needs the operate policy too.</param>
    /// <exception cref="KeyNotFoundException">No module is registered under <paramref name="moduleId"/>.</exception>
    public async Task<bool> CanAccessAsync(
        HttpContext context,
        ClaimsPrincipal user,
        AuthenticationProperties? session,
        string? moduleId,
        bool operate
    )
    {
        var config = auth.Get(_Name);

        if (config.Mode == AuthMode.None)
        {
            return true;
        }

        if (!_IsAuthenticated(user))
        {
            return false;
        }

        if (config.Mode != AuthMode.Host)
        {
            return true;
        }

        var passes = _SessionEvaluator(context, user, session);

        if (moduleId is not null)
        {
            var module = await _ModuleAccessAsync(moduleId, passes).ConfigureAwait(false);
            return operate ? module.CanOperate : module.CanView;
        }

        if (operate)
        {
            return await _ShellCanOperateAsync(config.Host, passes).ConfigureAwait(false);
        }

        return (await _FindViewAsync(config.Host, passes).ConfigureAwait(false)).Passed;
    }

    /// <summary>
    /// Returns the current request's session as the shell reports it to its page: whether a user is signed in, their
    /// name, and what they may do in each module. Under Basic and API key, a request without a session that sends
    /// the credential itself counts as signed in, as it does on every other shell endpoint.
    /// </summary>
    /// <param name="context">The current request.</param>
    /// <exception cref="InvalidOperationException">A module or the shell names a policy the host does not register.</exception>
    public async Task<DashboardSessionSnapshot> GetSessionSnapshotAsync(HttpContext context)
    {
        var config = auth.Get(_Name);

        switch (config.Mode)
        {
            case AuthMode.None:
                return _Everything(authenticated: true, name: null);

            case AuthMode.Host:
            {
                var session = await DashboardHostSession.AuthenticateAsync(context, _Name).ConfigureAwait(false);
                if (session is not { Succeeded: true, Principal: { } principal })
                {
                    return _Everything(authenticated: false, name: null);
                }

                var passes = _SessionEvaluator(context, principal, session.Properties);
                var modules = new Dictionary<string, DashboardModuleAccess>(
                    catalog.ModuleIds.Count,
                    StringComparer.Ordinal
                );
                foreach (var id in catalog.ModuleIds)
                {
                    modules[id] = await _ModuleAccessAsync(id, passes).ConfigureAwait(false);
                }

                return new DashboardSessionSnapshot
                {
                    Authenticated = true,
                    Name = principal.Identity?.Name,
                    CanOperate = await _ShellCanOperateAsync(config.Host, passes).ConfigureAwait(false),
                    Modules = modules,
                };
            }

            default:
            {
                var session = await DashboardHostSession.AuthenticateAsync(context, _Name).ConfigureAwait(false);
                if (session is { Succeeded: true, Principal: { } principal })
                {
                    return _Everything(authenticated: true, principal.Identity?.Name);
                }

                var result = await context
                    .RequestServices.GetRequiredKeyedService<IAuthService>(_Name)
                    .AuthenticateAsync(context, context.RequestAborted)
                    .ConfigureAwait(false);

                return result.IsAuthenticated
                    ? _Everything(authenticated: true, result.Username)
                    : _Everything(authenticated: false, name: null);
            }
        }
    }

    /// <summary>
    /// Checks whether a host user, authenticated by <paramref name="hostSchemes"/>, may hold a shell session: the
    /// shell's view policy is evaluated first, then each module's, and the first that passes is enough.
    /// </summary>
    /// <param name="context">The sign-in request.</param>
    /// <param name="user">The host user.</param>
    /// <param name="hostSchemes">The host schemes that authenticated <paramref name="user"/>.</param>
    /// <returns>Whether one view policy passed, and when none did, the name of every policy that refused.</returns>
    /// <exception cref="InvalidOperationException">A module or the shell names a policy the host does not register.</exception>
    internal Task<DashboardViewCheck> CheckSignInAsync(
        HttpContext context,
        ClaimsPrincipal user,
        IReadOnlyCollection<string> hostSchemes
    )
    {
        var passes = _Evaluator(policy =>
            DashboardHostAuthorization.PassesHostPolicyAsync(context, user, hostSchemes, policy)
        );

        return _FindViewAsync(auth.Get(_Name).Host, passes);
    }

    private async Task<DashboardViewCheck> _FindViewAsync(
        DashboardHostAuthenticationOptions host,
        Func<string?, Task<bool>> passes
    )
    {
        var candidates = new List<string?> { DashboardHostAuthorization.HostPolicyName(host, operate: false) };
        candidates.AddRange(catalog.ModuleIds.Select(id => catalog.GetPolicy(id).ViewPolicy));

        var refused = new List<string>();
        foreach (var policy in candidates)
        {
            if (await passes(policy).ConfigureAwait(false))
            {
                return new DashboardViewCheck { Passed = true, RefusedPolicies = [] };
            }

            var label = policy ?? DefaultPolicyLabel;
            if (!refused.Contains(label, StringComparer.Ordinal))
            {
                refused.Add(label);
            }
        }

        return new DashboardViewCheck { Passed = false, RefusedPolicies = refused };
    }

    private async Task<DashboardModuleAccess> _ModuleAccessAsync(string moduleId, Func<string?, Task<bool>> passes)
    {
        var policy = catalog.GetPolicy(moduleId);
        var canView = await passes(policy.ViewPolicy).ConfigureAwait(false);
        var canOperate = canView && await passes(policy.OperatePolicy).ConfigureAwait(false);

        return new DashboardModuleAccess { CanView = canView, CanOperate = canOperate };
    }

    private static async Task<bool> _ShellCanOperateAsync(
        DashboardHostAuthenticationOptions host,
        Func<string?, Task<bool>> passes
    )
    {
        return await passes(DashboardHostAuthorization.HostPolicyName(host, operate: false)).ConfigureAwait(false)
            && await passes(DashboardHostAuthorization.HostPolicyName(host, operate: true)).ConfigureAwait(false);
    }

    private DashboardSessionSnapshot _Everything(bool authenticated, string? name)
    {
        var access = new DashboardModuleAccess { CanView = authenticated, CanOperate = authenticated };

        return new DashboardSessionSnapshot
        {
            Authenticated = authenticated,
            Name = name,
            CanOperate = authenticated,
            Modules = catalog.ModuleIds.ToDictionary(static id => id, _ => access, StringComparer.Ordinal),
        };
    }

    private static Func<string?, Task<bool>> _SessionEvaluator(
        HttpContext context,
        ClaimsPrincipal user,
        AuthenticationProperties? session
    )
    {
        if (!_IsAuthenticated(user))
        {
            return static _ => Task.FromResult(false);
        }

        return _Evaluator(policy => DashboardHostAuthorization.PassesHostPolicyAsync(context, user, session, policy));
    }

    // Modules that inherit the shell's policies name the same policy, so each distinct policy is evaluated once.
    private static Func<string?, Task<bool>> _Evaluator(Func<string?, Task<bool>> evaluate)
    {
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);

        return async policy =>
        {
            // Policy names are never empty (an empty one means the default), so the empty key stands for the default.
            var key = policy ?? string.Empty;
            if (!results.TryGetValue(key, out var passed))
            {
                passed = await evaluate(policy).ConfigureAwait(false);
                results[key] = passed;
            }

            return passed;
        };
    }

    private static bool _IsAuthenticated(ClaimsPrincipal user)
    {
        return user.Identities.Any(static identity => identity.IsAuthenticated);
    }
}

/// <summary>What one user may do in one module.</summary>
internal sealed record DashboardModuleAccess
{
    /// <summary>Gets a value indicating whether the user may see the module and read from it.</summary>
    public required bool CanView { get; init; }

    /// <summary>Gets a value indicating whether the user may run the module's state-changing requests.</summary>
    public required bool CanOperate { get; init; }
}

/// <summary>The current request's session, as the shell reports it to its page and on <c>api/auth/info</c>.</summary>
internal sealed record DashboardSessionSnapshot
{
    /// <summary>Gets a value indicating whether a user is signed in.</summary>
    public required bool Authenticated { get; init; }

    /// <summary>Gets the signed-in user's name, or <see langword="null"/> when there is none.</summary>
    public required string? Name { get; init; }

    /// <summary>Gets a value indicating whether the user passes the shell's own operate policy.</summary>
    public required bool CanOperate { get; init; }

    /// <summary>Gets what the user may do in each registered module, keyed by module id.</summary>
    public required IReadOnlyDictionary<string, DashboardModuleAccess> Modules { get; init; }

    /// <summary>Returns whether the user may view the module; a module missing from the map is one they may not.</summary>
    public bool MayView(string moduleId) => Modules.TryGetValue(moduleId, out var access) && access.CanView;

    /// <summary>Returns whether the user may operate the module.</summary>
    public bool MayOperate(string moduleId) => Modules.TryGetValue(moduleId, out var access) && access.CanOperate;
}

/// <summary>The outcome of checking whether a host user may hold a shell session.</summary>
internal sealed record DashboardViewCheck
{
    /// <summary>Gets a value indicating whether the shell's or one module's view policy passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>Gets every policy that refused the user when none passed; empty when one passed.</summary>
    public required IReadOnlyList<string> RefusedPolicies { get; init; }
}

/// <summary>Protects the shell's endpoints with the shell's authentication and the per-module policies.</summary>
internal static class DashboardShellAccess
{
    /// <summary>
    /// Protects every endpoint of <paramref name="group"/> as an endpoint of the module <paramref name="moduleId"/>, or
    /// of the shell itself when it is <see langword="null"/>. An endpoint that carries
    /// <see cref="DashboardOperateMetadata"/> also requires the operate policy of the module that metadata names.
    /// </summary>
    internal static RouteGroupBuilder RequireDashboardShellAccess(this RouteGroupBuilder group, string? moduleId)
    {
        _Apply(group, moduleId);
        return group;
    }

    /// <summary>
    /// Protects the endpoint of <paramref name="endpoint"/> as an endpoint of the module <paramref name="moduleId"/>,
    /// or of the shell itself when it is <see langword="null"/>.
    /// </summary>
    internal static RouteHandlerBuilder RequireDashboardShellAccess(this RouteHandlerBuilder endpoint, string? moduleId)
    {
        _Apply(endpoint, moduleId);
        return endpoint;
    }

    // Only builders that run Finally conventions are accepted: the interface's default Finally does nothing, which
    // would publish the endpoint unprotected. Finally runs after the endpoint's own conventions, so it sees the
    // operate metadata that RequireOperate adds after the group's conventions were declared.
    private static void _Apply(IEndpointConventionBuilder builder, string? moduleId)
    {
        builder.Finally(endpoint =>
        {
            var operate = endpoint.Metadata.OfType<DashboardOperateMetadata>().LastOrDefault();
            var module = operate?.ModuleId ?? moduleId;
            var config = endpoint
                .ApplicationServices.GetRequiredService<IOptionsMonitor<AuthConfig>>()
                .Get(SetupDashboard.AuthenticationName);
            var session = DashboardHostSession.SchemeName(SetupDashboard.AuthenticationName);

            switch (config.Mode)
            {
                case AuthMode.None:
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
                    break;

                case AuthMode.Host:
                    endpoint.Metadata.Add(
                        new AuthorizationPolicyBuilder(session)
                            .RequireAuthenticatedUser()
                            .AddRequirements(new DashboardAccessRequirement(module, operate is not null))
                            .Build()
                    );
                    break;

                default:
                    // The session cookie serves the browser; the credential header still serves scripts.
                    endpoint.Metadata.Add(
                        new DashboardEndpointMetadata(SetupDashboard.AuthenticationName) { AcceptsSession = true }
                    );
                    endpoint.Metadata.Add(
                        new AuthorizationPolicyBuilder(session, SetupDashboard.AuthenticationName)
                            .RequireAuthenticatedUser()
                            .Build()
                    );
                    break;
            }
        });
    }
}

/// <summary>Requires the session to pass a module's view or operate policy, or any view policy for the shell's own.</summary>
/// <param name="ModuleId">The module the endpoint belongs to, or <see langword="null"/> for the shell's own.</param>
/// <param name="Operate">Whether the endpoint also needs the operate policy.</param>
internal sealed record DashboardAccessRequirement(string? ModuleId, bool Operate) : IAuthorizationRequirement;

/// <summary>Succeeds a <see cref="DashboardAccessRequirement"/> through <see cref="DashboardPermissions"/>.</summary>
/// <remarks>
/// Services come from the request, not the constructor: <see cref="IAuthorizationService"/> depends on every
/// registered handler, so a handler that took the permissions in its constructor would form a dependency cycle.
/// </remarks>
internal sealed class DashboardAccessHandler : AuthorizationHandler<DashboardAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DashboardAccessRequirement requirement
    )
    {
        if (context.Resource is not HttpContext httpContext)
        {
            return;
        }

        var session = await DashboardHostSession
            .AuthenticateAsync(httpContext, SetupDashboard.AuthenticationName)
            .ConfigureAwait(false);
        var permissions = httpContext.RequestServices.GetRequiredService<DashboardPermissions>();

        if (
            await permissions
                .CanAccessAsync(
                    httpContext,
                    context.User,
                    session.Properties,
                    requirement.ModuleId,
                    requirement.Operate
                )
                .ConfigureAwait(false)
        )
        {
            context.Succeed(requirement);
        }
    }
}
