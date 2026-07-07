// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Api.ServiceDefaults;

/// <summary>Options for <see cref="SetupApi.MapHeadlessEndpoints(WebApplication, Action{HeadlessApiDefaultEndpointOptions}?)"/>.</summary>
[PublicAPI]
public sealed class HeadlessApiDefaultEndpointOptions
{
    internal const string AppliedKey = "Headless.Api.MapHeadlessEndpoints.Applied";

    /// <summary>The default aggregate health endpoint path.</summary>
    public const string DefaultHealthPath = "/health";

    /// <summary>The default liveness endpoint path.</summary>
    public const string DefaultAlivePath = "/alive";

    /// <summary>Whether to map the aggregate health endpoint.</summary>
    public bool MapHealthEndpoint { get; set; } = true;

    /// <summary>Whether to map the liveness endpoint.</summary>
    public bool MapAliveEndpoint { get; set; } = true;

    /// <summary>The aggregate health endpoint path.</summary>
    public string HealthPath { get; set; } = DefaultHealthPath;

    /// <summary>The liveness endpoint path.</summary>
    public string AlivePath { get; set; } = DefaultAlivePath;

    /// <summary>The health-check tag used to filter which checks appear on the liveness endpoint.</summary>
    /// <remarks>Only health checks registered with this tag are included in the <see cref="AlivePath"/> response. All checks appear on <see cref="HealthPath"/>.</remarks>
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
    /// <remarks>
    /// The default implementation (<see cref="SetupApi.WriteHealthReportAsync"/>) buffers the JSON body
    /// before writing to the response, so a serialization failure returns 500 rather than a partial body.
    /// </remarks>
    public Func<HttpContext, HealthReport, Task> HealthResponseWriter { get; set; } = SetupApi.WriteHealthReportAsync;
}
