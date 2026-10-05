// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// Refuses to run an endpoint that carries <see cref="IAuthorizationRequirementData"/> (such as
/// <c>[RequiresFeature]</c>) when the authorization middleware never saw that endpoint, the way ASP.NET Core's
/// endpoint middleware already refuses an <see cref="IAuthorizeData"/> endpoint.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core's check covers <see cref="IAuthorizeData"/> only, so requirement data is skipped silently when the
/// pipeline lacks <c>UseAuthorization()</c> after routing, for example when a host calls <c>UseRouting()</c> itself
/// and leaves authorization to the middleware <c>WebApplication</c> adds ahead of routing. No metadata the built-in
/// check recognizes is free of side effects: any <see cref="IAuthorizeData"/> adds the default policy or drops the
/// fallback policy, and the CORS and antiforgery markers demand their own middleware.
/// </para>
/// <para>
/// The guard is a matcher policy because routing is the one place that sees every endpoint, MVC and Minimal API,
/// attribute or convention, before it runs. It swaps the selected endpoint for one with the same metadata whose
/// request delegate checks the marker the authorization middleware leaves on the request, then delegates to the
/// original. It honors <c>RouteOptions.SuppressCheckForUnhandledSecurityMetadata</c>, like the built-in check.
/// </para>
/// </remarks>
internal sealed class AuthorizationRequirementDataGuardMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    // AuthorizationMiddleware sets this item when it observes an endpoint; ASP.NET Core keeps the constant internal
    // (AuthorizationMiddleware / EndpointMiddleware), so its value is mirrored here.
    private const string _AuthorizationMiddlewareInvokedKey = "__AuthorizationMiddlewareWithEndpointInvoked";

    // Keyed weakly on the original endpoint, so a reloaded data source does not keep stale endpoints alive, and the
    // guarded endpoint stays the same instance per original (the authorization policy cache keys on it).
    private readonly ConditionalWeakTable<Endpoint, Endpoint> _guarded = [];
    private readonly bool _suppressed;

    public AuthorizationRequirementDataGuardMatcherPolicy(IOptions<RouteOptions> routeOptions)
    {
        Argument.IsNotNull(routeOptions);

        _suppressed = routeOptions.Value.SuppressCheckForUnhandledSecurityMetadata;
    }

    /// <summary>Runs last, so it wraps the endpoint other policies (such as dynamic controllers) finally selected.</summary>
    public override int Order => int.MaxValue;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
    {
        if (_suppressed)
        {
            return false;
        }

        foreach (var endpoint in endpoints)
        {
            if (_NeedsGuard(endpoint))
            {
                return true;
            }
        }

        return false;
    }

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!candidates.IsValidCandidate(i))
            {
                continue;
            }

            ref var candidate = ref candidates[i];

            if (_NeedsGuard(candidate.Endpoint))
            {
                candidates.ReplaceEndpoint(i, _guarded.GetValue(candidate.Endpoint, _Guard), candidate.Values);
            }
        }

        return Task.CompletedTask;
    }

    private static bool _NeedsGuard(Endpoint endpoint)
    {
        return endpoint.RequestDelegate is not null
            && endpoint.Metadata.GetMetadata<IAuthorizationRequirementData>() is not null
            && endpoint.Metadata.GetMetadata<GuardedEndpointMarker>() is null;
    }

    private static Endpoint _Guard(Endpoint endpoint)
    {
        var inner = endpoint.RequestDelegate!;

        Task guarded(HttpContext context)
        {
            if (!context.Items.ContainsKey(_AuthorizationMiddlewareInvokedKey))
            {
                throw new InvalidOperationException(
                    $"Endpoint {endpoint.DisplayName} contains authorization requirements (such as [RequiresFeature]), "
                        + "but the authorization middleware did not run for it. Add app.UseAuthorization() after "
                        + "app.UseRouting() and before the endpoints."
                );
            }

            return inner(context);
        }

        var metadata = new EndpointMetadataCollection([.. endpoint.Metadata, GuardedEndpointMarker.Instance]);

        return endpoint is RouteEndpoint route
            ? new RouteEndpoint(guarded, route.RoutePattern, route.Order, metadata, route.DisplayName)
            : new Endpoint(guarded, metadata, endpoint.DisplayName);
    }

    /// <summary>Marks a guarded endpoint, so a second matcher pass never wraps it twice.</summary>
    private sealed class GuardedEndpointMarker
    {
        public static GuardedEndpointMarker Instance { get; } = new();
    }
}
