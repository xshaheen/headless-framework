// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>HttpClient defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsHttpClientOptions
{
    /// <summary>Whether to add the standard resilience handler to default HttpClient builders.</summary>
    public bool UseStandardResilienceHandler { get; set; } = true;

    /// <summary>Whether to register service discovery and enable it for default HttpClient builders.</summary>
    public bool UseServiceDiscovery { get; set; } = true;

    /// <summary>Whether to add a default User-Agent header based on the host application name.</summary>
    public bool AddApplicationUserAgent { get; set; } = true;
}
