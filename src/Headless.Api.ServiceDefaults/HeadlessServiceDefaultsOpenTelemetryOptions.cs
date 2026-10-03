// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>
/// OpenTelemetry defaults for Headless API services.
/// <para>
/// Knobs (in order of increasing specificity):
/// <list type="bullet">
///   <item><see cref="RecordException"/> — toggle exception recording on spans globally.</item>
///   <item><see cref="Filter"/> — replace the entire tracing filter with a custom predicate; compose
///         <see cref="SkipOperationalEndpointFunc"/> if you want to keep the default operational-path skip.</item>
///   <item><see cref="SkipOperationalEndpointFunc"/> — read-only during tracing; refreshed atomically by
///         <c>MapHeadlessEndpoints()</c> once the actual health/alive paths are known.</item>
///   <item><see cref="ConfigureAspNetCoreInstrumentation"/> — last-resort hook for full control over
///         <c>AspNetCoreTraceInstrumentationOptions</c>; runs after all framework defaults.</item>
/// </list>
/// </para>
/// </summary>
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
    /// Predicate that decides whether a request is traced. Return <see langword="true"/> to trace, <see langword="false"/> to skip.
    /// When <see langword="null"/> (the default), the framework skips the operational health and liveness endpoints
    /// (paths sourced from <see cref="HeadlessApiDefaultEndpointOptions.HealthPath"/> and
    /// <see cref="HeadlessApiDefaultEndpointOptions.AlivePath"/> at <c>MapHeadlessEndpoints()</c> time, or
    /// from the <see cref="HeadlessApiDefaultEndpointOptions.DefaultHealthPath"/> /
    /// <see cref="HeadlessApiDefaultEndpointOptions.DefaultAlivePath"/> constants until then).
    /// Setting a custom <c>Filter</c> fully replaces the default — compose <see cref="SkipOperationalEndpointFunc"/>
    /// from your predicate if you want to keep the default skip behavior.
    /// For broader knobs (enrichers, exception recording, instrumentation toggles) use
    /// <see cref="ConfigureAspNetCoreInstrumentation"/>.
    /// </summary>
    public Func<HttpContext, bool>? Filter { get; set; }

    /// <summary>
    /// Default skip predicate: returns <see langword="true"/> when the request targets a mapped operational endpoint
    /// (health or alive). Set by <c>MapHeadlessEndpoints()</c> once the actual paths are known; available
    /// immediately with the default paths (<see cref="HeadlessApiDefaultEndpointOptions.DefaultHealthPath"/> /
    /// <see cref="HeadlessApiDefaultEndpointOptions.DefaultAlivePath"/>) until then.
    /// Compose this into a custom <see cref="Filter"/> to preserve the default skip behavior while adding
    /// your own rules — e.g. <c>Filter = ctx =&gt; !opts.SkipOperationalEndpointFunc(ctx) &amp;&amp; ...</c>.
    /// </summary>
    [PublicAPI]
    public Func<HttpContext, bool> SkipOperationalEndpointFunc { get; internal set; } =
        BuildSkipFunc(
            healthPath: HeadlessApiDefaultEndpointOptions.DefaultHealthPath,
            alivePath: HeadlessApiDefaultEndpointOptions.DefaultAlivePath,
            healthMapped: true,
            aliveMapped: true
        );

    /// <summary>
    /// Final hook to tune ASP.NET Core trace instrumentation (<see cref="AspNetCoreTraceInstrumentationOptions.Filter"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.EnrichWithHttpRequest"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.EnrichWithHttpResponse"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.EnrichWithException"/>,
    /// <see cref="AspNetCoreTraceInstrumentationOptions.RecordException"/>, etc.).
    /// Runs AFTER framework defaults so callers can override or compose any setting.
    /// </summary>
    public Action<AspNetCoreTraceInstrumentationOptions>? ConfigureAspNetCoreInstrumentation { get; set; }

    /// <summary>Whether to record exceptions on spans. Defaults to <see langword="true"/>.</summary>
    public bool RecordException { get; set; } = true;

    internal static Func<HttpContext, bool> BuildSkipFunc(
        string healthPath,
        string alivePath,
        bool healthMapped,
        bool aliveMapped
    )
    {
        return context =>
        {
            var path = context.Request.Path.Value;

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (healthMapped && string.Equals(path, healthPath, StringComparison.Ordinal))
            {
                return true;
            }

            if (aliveMapped && string.Equals(path, alivePath, StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        };
    }
}
