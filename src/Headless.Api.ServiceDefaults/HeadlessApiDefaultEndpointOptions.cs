// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Api;

/// <summary>Options for <see cref="ApiSetup.MapHeadlessEndpoints(WebApplication, Action{HeadlessApiDefaultEndpointOptions}?)"/>.</summary>
[PublicAPI]
public sealed class HeadlessApiDefaultEndpointOptions
{
    internal const string AppliedKey = "Headless.Api.MapHeadlessEndpoints.Applied";

    /// <summary>Whether to map the aggregate health endpoint.</summary>
    public bool MapHealthEndpoint { get; set; } = true;

    /// <summary>Whether to map the liveness endpoint.</summary>
    public bool MapAliveEndpoint { get; set; } = true;

    /// <summary>The aggregate health endpoint path.</summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>The liveness endpoint path.</summary>
    public string AlivePath { get; set; } = "/alive";

    /// <summary>The health-check tag included in the liveness endpoint.</summary>
    public string AliveTag { get; set; } = "live";

    /// <summary>The health endpoint route name.</summary>
    public string HealthEndpointName { get; set; } = "HealthCheck";

    /// <summary>The liveness endpoint route name.</summary>
    public string AliveEndpointName { get; set; } = "AliveCheck";

    /// <summary>Whether operational endpoints allow anonymous requests.</summary>
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>Whether operational endpoints are excluded from OpenAPI descriptions.</summary>
    public bool ExcludeFromDescription { get; set; } = true;

    /// <summary>The response writer used by the aggregate health endpoint.</summary>
    public Func<HttpContext, HealthReport, Task> HealthResponseWriter { get; set; } = ApiSetup.WriteHealthReportAsync;

}
