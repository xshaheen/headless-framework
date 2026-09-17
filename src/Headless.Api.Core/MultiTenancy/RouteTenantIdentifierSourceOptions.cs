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
