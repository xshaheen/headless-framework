// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// <see cref="LinkGenerator"/> decorator that keeps the tenant route segment in generated links (R13):
/// when the explicit values lack the route value named by
/// <see cref="RouteTenantIdentifierSourceOptions.RouteValueName"/>, the current request's value is
/// promoted into a copy of the explicit values before delegating to the wrapped generator.
/// </summary>
/// <remarks>
/// <para>
/// Why promotion is needed (U5 spike evidence, KTD7): ASP.NET Core discards every ambient route
/// value once a required value (controller, action, page) differs from the current request, so
/// <c>Url.Action</c> and <c>GetPathByAction</c> lose a leading <c>{tenant}</c> segment whenever they
/// link to another action, and the endpoint-name scheme (<c>GetPathByName</c>) passes no ambient
/// values at all. Only the route-values scheme (<c>GetPathByRouteValues</c>) keeps the value on its
/// own. Promoting the value to an explicit one sidesteps the ambient-invalidation rule on every
/// surface.
/// </para>
/// <para>
/// Why the request's route values are consulted after the ambient dictionary: the endpoint-name
/// extensions pass <see langword="null"/> ambient values, so the ambient dictionary alone cannot
/// recover the tenant there. <c>HttpContext.Request.RouteValues</c> is the very source the ambient
/// dictionary is built from on the other surfaces, so falling back to it promotes the same value
/// consistently.
/// </para>
/// <para>
/// Boundaries: an explicit entry for the configured name — even one holding <see langword="null"/> —
/// always wins and is passed through untouched; only the configured name is promoted; the two
/// overloads without an <see cref="HttpContext"/> have no ambient request and pass through
/// unchanged; the caller's dictionaries are never mutated. The opt-out
/// (<see cref="RouteTenantIdentifierSourceOptions.PromoteAmbientRouteValue"/>) is read at call time
/// rather than at registration: the option may be contributed through any <c>AddRouteSource</c>
/// overload, including an <c>IConfiguration</c> bind whose value is unknown while services are
/// being registered, so the decorator is always installed with the route source and becomes a
/// transparent pass-through when promotion is disabled.
/// </para>
/// </remarks>
internal sealed class TenantAmbientRouteValueLinkGenerator(
    LinkGenerator inner,
    IOptions<RouteTenantIdentifierSourceOptions> options
) : LinkGenerator
{
    private readonly IOptions<RouteTenantIdentifierSourceOptions> _options = Argument.IsNotNull(options);

    /// <summary>The wrapped generator; exposed so registration tests can prove the wrap happened exactly once.</summary>
    internal LinkGenerator Inner { get; } = Argument.IsNotNull(inner);

    /// <inheritdoc/>
    public override string? GetPathByAddress<TAddress>(
        HttpContext httpContext,
        TAddress address,
        RouteValueDictionary values,
        RouteValueDictionary? ambientValues = null,
        PathString? pathBase = null,
        FragmentString fragment = default,
        LinkOptions? options = null
    )
    {
        Argument.IsNotNull(httpContext);
        Argument.IsNotNull(values);

        return Inner.GetPathByAddress(
            httpContext,
            address,
            _PromoteTenant(httpContext, values, ambientValues),
            ambientValues,
            pathBase,
            fragment,
            options
        );
    }

    /// <inheritdoc/>
    public override string? GetPathByAddress<TAddress>(
        TAddress address,
        RouteValueDictionary values,
        PathString pathBase = default,
        FragmentString fragment = default,
        LinkOptions? options = null
    )
    {
        // No request, so nothing ambient exists to promote.
        return Inner.GetPathByAddress(address, values, pathBase, fragment, options);
    }

    /// <inheritdoc/>
    public override string? GetUriByAddress<TAddress>(
        HttpContext httpContext,
        TAddress address,
        RouteValueDictionary values,
        RouteValueDictionary? ambientValues = null,
        string? scheme = null,
        HostString? host = null,
        PathString? pathBase = null,
        FragmentString fragment = default,
        LinkOptions? options = null
    )
    {
        Argument.IsNotNull(httpContext);
        Argument.IsNotNull(values);

        return Inner.GetUriByAddress(
            httpContext,
            address,
            _PromoteTenant(httpContext, values, ambientValues),
            ambientValues,
            scheme,
            host,
            pathBase,
            fragment,
            options
        );
    }

    /// <inheritdoc/>
    public override string? GetUriByAddress<TAddress>(
        TAddress address,
        RouteValueDictionary values,
        string scheme,
        HostString host,
        PathString pathBase = default,
        FragmentString fragment = default,
        LinkOptions? options = null
    )
    {
        // No request, so nothing ambient exists to promote.
        return Inner.GetUriByAddress(address, values, scheme, host, pathBase, fragment, options);
    }

    /// <summary>
    /// Returns <paramref name="values"/> itself when nothing is promoted, or a copy carrying the
    /// tenant value found in <paramref name="ambientValues"/> (first) or the request's route values.
    /// </summary>
    private RouteValueDictionary _PromoteTenant(
        HttpContext httpContext,
        RouteValueDictionary values,
        RouteValueDictionary? ambientValues
    )
    {
        var routeOptions = _options.Value;

        if (!routeOptions.PromoteAmbientRouteValue)
        {
            return values;
        }

        var name = routeOptions.RouteValueName;

        // An explicit entry — a different tenant or a deliberate null — is the caller's decision.
        if (values.ContainsKey(name))
        {
            return values;
        }

        var tenant = ambientValues?[name] ?? httpContext.Request.RouteValues[name];

        if (tenant is null)
        {
            return values;
        }

        return new RouteValueDictionary(values) { [name] = tenant };
    }

    /// <summary>
    /// Registration marker: its presence in the service collection records that the
    /// <see cref="LinkGenerator"/> was already wrapped, so repeated <c>AddRouteSource</c> calls never
    /// nest a second decorator.
    /// </summary>
    internal sealed class RegistrationMarker
    {
        public static readonly RegistrationMarker Instance = new();

        private RegistrationMarker() { }
    }
}
