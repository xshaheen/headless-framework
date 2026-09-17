// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Options for the route tenant identifier source (R2, R7): the name of the route value whose
/// string content is yielded as the raw identifier — for example <c>tenant</c> for an endpoint
/// mapped at <c>/{tenant}/orders</c>.
/// </summary>
/// <remarks>
/// Requires <c>UseHeadlessTenantCatalogResolution()</c> to run after <c>UseRouting()</c>: route
/// values only exist once routing has matched. A misordered pipeline makes this source find
/// nothing on every request, which the middleware reports through its route-source misorder
/// event. Options contributions accumulate across <c>AddRouteSource</c> calls, so for this
/// single-valued option the last contribution wins (KTD3). The source never shape-validates the
/// captured value; <c>ITenantCatalogService</c> owns normalization.
/// </remarks>
[PublicAPI]
public sealed class RouteTenantIdentifierSourceOptions
{
    /// <summary>The route value name holding the tenant identifier; the route pattern must capture it.</summary>
    public string RouteValueName { get; set; } = DefaultRouteValueName;

    /// <summary>
    /// Whether generated links keep the current request's tenant route value when the caller
    /// supplies none (R13). Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registering the route source wraps the routing <c>LinkGenerator</c> so that <c>Url.Action</c>,
    /// <c>GetPathByAction</c>, and <c>GetPathByName</c> — which ASP.NET Core would otherwise generate
    /// without the leading <c>{tenant}</c> segment when linking to another endpoint — promote the
    /// current request's value named by <see cref="RouteValueName"/> into the explicit values. An
    /// explicit value for that name always wins, so a link to a different tenant is unaffected.
    /// </para>
    /// <para>
    /// The switch is honored at link-generation time, not at registration: the wrapper is always
    /// installed with the route source (through every <c>AddRouteSource</c> overload, including an
    /// <c>IConfiguration</c> bind resolved later) and passes every call through untouched when this
    /// is <see langword="false"/>, restoring the ASP.NET Core default behavior.
    /// </para>
    /// </remarks>
    public bool PromoteAmbientRouteValue { get; set; } = true;

    /// <summary>The default <see cref="RouteValueName"/> when no configuration contribution overrides it.</summary>
    public const string DefaultRouteValueName = "tenant";
}

/// <summary>Validator for <see cref="RouteTenantIdentifierSourceOptions"/> (R7).</summary>
internal sealed class RouteTenantIdentifierSourceOptionsValidator
    : AbstractValidator<RouteTenantIdentifierSourceOptions>
{
    public RouteTenantIdentifierSourceOptionsValidator()
    {
        RuleFor(x => x.RouteValueName).NotEmpty();
    }
}
