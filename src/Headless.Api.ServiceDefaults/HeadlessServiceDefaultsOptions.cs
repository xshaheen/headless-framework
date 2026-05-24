// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api;

/// <summary>Options for configuring Headless API service defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsOptions
{
    internal bool UseHeadlessCalled { get; set; }

    internal bool MapHeadlessEndpointsCalled { get; set; }

    /// <summary>Startup validation defaults.</summary>
    public HeadlessServiceDefaultsValidationOptions Validation { get; } = new();

    /// <summary>OpenTelemetry defaults.</summary>
    public HeadlessServiceDefaultsOpenTelemetryOptions OpenTelemetry { get; } = new();

    /// <summary>OpenAPI service-registration defaults.</summary>
    public HeadlessServiceDefaultsOpenApiOptions OpenApi { get; } = new();

    /// <summary>Static web asset defaults.</summary>
    public HeadlessServiceDefaultsStaticAssetsOptions StaticAssets { get; } = new();

    /// <summary>HttpClient defaults.</summary>
    public HeadlessServiceDefaultsHttpClientOptions HttpClient { get; } = new();

    /// <summary>Antiforgery defaults. Opt-in: most APIs use bearer-token auth and don't need CSRF protection.</summary>
    public HeadlessServiceDefaultsAntiforgeryOptions Antiforgery { get; } = new();
}

/// <summary>Startup validation defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsValidationOptions
{
    /// <summary>Whether to validate the service provider when the host starts.</summary>
    public bool ValidateServiceProviderOnStartup { get; set; } = true;

    /// <summary>Whether startup should fail when <c>UseHeadless()</c> was not applied.</summary>
    public bool RequireUseHeadless { get; set; } = true;

    /// <summary>Whether startup should fail when <c>MapHeadlessEndpoints()</c> was not applied.</summary>
    public bool RequireMapHeadlessEndpoints { get; set; } = true;
}

/// <summary>OpenAPI service-registration defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsOpenApiOptions
{
    /// <summary>Whether to register OpenAPI document generation.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Allows callers to tune ASP.NET Core OpenAPI options.</summary>
    public Action<OpenApiOptions>? ConfigureOpenApi { get; set; }

    /// <summary>The route pattern for OpenAPI JSON documents.</summary>
    [StringSyntax("Route")]
    public string RoutePattern { get; set; } = "/openapi/{documentName}.json";

    /// <summary>Whether to attach output-cache metadata to OpenAPI document endpoints.</summary>
    public bool CacheDocument { get; set; } = true;
}

/// <summary>Static web asset defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsStaticAssetsOptions
{
    /// <summary>Whether to map static web assets when the generated manifest exists.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>OpenTelemetry defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsOpenTelemetryOptions
{
    /// <summary>Whether to register OpenTelemetry logging, metrics, and tracing defaults.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to use OTLP exporter when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured.</summary>
    public bool UseOtlpExporterWhenEndpointConfigured { get; set; } = true;

    /// <summary>Allows callers to tune OpenTelemetry logging.</summary>
    public Action<OpenTelemetryLoggerOptions>? ConfigureLogging { get; set; }

    /// <summary>Allows callers to tune OpenTelemetry metrics.</summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }

    /// <summary>Allows callers to tune OpenTelemetry tracing.</summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// Predicate that decides whether a request is traced. Return <c>true</c> to trace, <c>false</c> to skip.
    /// When <c>null</c> (the default), the framework skips the operational health and liveness endpoints
    /// (paths sourced from <see cref="HeadlessApiDefaultEndpointOptions.HealthPath"/> and
    /// <see cref="HeadlessApiDefaultEndpointOptions.AlivePath"/> at <c>MapHeadlessEndpoints()</c> time, or
    /// from the <see cref="HeadlessApiDefaultEndpointOptions.DefaultHealthPath"/> /
    /// <see cref="HeadlessApiDefaultEndpointOptions.DefaultAlivePath"/> constants until then).
    /// Setting a custom <c>Filter</c> fully replaces the default — call
    /// <see cref="ShouldSkipOperationalEndpoint"/> from your predicate if you want to keep the default skip behavior.
    /// For broader knobs (enrichers, exception recording, instrumentation toggles) use
    /// <see cref="ConfigureAspNetCoreInstrumentation"/>.
    /// </summary>
    public Func<HttpContext, bool>? Filter { get; set; }

    /// <summary>Health endpoint path used by the default tracing filter. Updated by <c>MapHeadlessEndpoints()</c>.</summary>
    internal string OperationalHealthPath { get; set; } = HeadlessApiDefaultEndpointOptions.DefaultHealthPath;

    /// <summary>Alive endpoint path used by the default tracing filter. Updated by <c>MapHeadlessEndpoints()</c>.</summary>
    internal string OperationalAlivePath { get; set; } = HeadlessApiDefaultEndpointOptions.DefaultAlivePath;

    /// <summary>Whether the health endpoint is mapped and should be excluded from tracing.</summary>
    internal bool OperationalHealthMapped { get; set; } = true;

    /// <summary>Whether the alive endpoint is mapped and should be excluded from tracing.</summary>
    internal bool OperationalAliveMapped { get; set; } = true;

    /// <summary>
    /// Default skip predicate: returns <c>true</c> when the request targets a mapped operational endpoint
    /// (health or alive). Exposed so custom <see cref="Filter"/> implementations can compose the framework
    /// default with additional rules — e.g. <c>Filter = ctx =&gt; !opts.ShouldSkipOperationalEndpoint(ctx) &amp;&amp; ...</c>.
    /// </summary>
    [PublicAPI]
    public bool ShouldSkipOperationalEndpoint(HttpContext context)
    {
        var path = context.Request.Path.Value;

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (OperationalHealthMapped && string.Equals(path, OperationalHealthPath, StringComparison.Ordinal))
        {
            return true;
        }

        if (OperationalAliveMapped && string.Equals(path, OperationalAlivePath, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Final hook to tune ASP.NET Core trace instrumentation (<see cref="AspNetCoreTraceInstrumentationOptions.Filter"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.EnrichWithHttpRequest"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.EnrichWithHttpResponse"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.EnrichWithException"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.RecordException"/>, etc.).
    /// Runs AFTER framework defaults so callers can override or compose any setting.
    /// </summary>
    public Action<AspNetCoreTraceInstrumentationOptions>? ConfigureAspNetCoreInstrumentation { get; set; }
}

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

/// <summary>Antiforgery defaults.</summary>
/// <remarks>
/// CSRF protection only applies to cookie-based authentication. For bearer-token / API-key / OAuth APIs the browser
/// does not auto-attach credentials cross-origin, so antiforgery is not needed and adds latency. Enable explicitly
/// when the API uses cookie-based auth (ASP.NET Core Identity cookies, Server-rendered MVC sessions, etc.).
/// Consumers wire the middleware themselves via <c>app.UseAntiforgery()</c> after <c>UseAuthentication()</c>/<c>UseAuthorization()</c>.
/// </remarks>
[PublicAPI]
public sealed class HeadlessServiceDefaultsAntiforgeryOptions
{
    /// <summary>Whether <c>AddHeadless()</c> should register the antiforgery service. Default: <c>false</c>.</summary>
    public bool Enabled { get; set; }
}
