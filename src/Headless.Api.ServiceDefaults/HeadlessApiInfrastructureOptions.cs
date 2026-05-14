// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api;

/// <summary>Options for configuring Headless API service defaults.</summary>
[PublicAPI]
public sealed class HeadlessApiInfrastructureOptions
{
    internal bool UseHeadlessDefaultsCalled { get; set; }

    internal bool MapHeadlessDefaultEndpointsCalled { get; set; }

    /// <summary>Whether to validate the dependency container when the host starts.</summary>
    public bool ValidateDependencyContainerOnStartup { get; set; } = true;

    /// <summary>Whether startup should fail when <c>UseHeadlessDefaults()</c> was not applied.</summary>
    public bool ValidateUseHeadlessDefaultsOnStartup { get; set; }

    /// <summary>Whether startup should fail when <c>MapHeadlessDefaultEndpoints()</c> was not applied.</summary>
    public bool ValidateMapHeadlessDefaultEndpointsOnStartup { get; set; }

    /// <summary>OpenTelemetry defaults.</summary>
    public HeadlessApiOpenTelemetryOptions OpenTelemetry { get; } = new();

    /// <summary>OpenAPI service-registration defaults.</summary>
    public HeadlessApiOpenApiOptions OpenApi { get; } = new();

    /// <summary>HttpClient defaults.</summary>
    public HeadlessApiHttpClientOptions HttpClient { get; } = new();

    /// <summary>Whether to register ASP.NET Core antiforgery services.</summary>
    public bool AddAntiforgery { get; set; } = true;

    /// <summary>The health-check tag used for the default liveness check.</summary>
    public string AliveTag { get; set; } = "live";

    /// <summary>Allows callers to tune MVC and Minimal API JSON options.</summary>
    public Action<JsonSerializerOptions>? ConfigureJsonOptions { get; set; }
}

/// <summary>OpenAPI service-registration defaults.</summary>
[PublicAPI]
public sealed class HeadlessApiOpenApiOptions
{
    /// <summary>Whether to register OpenAPI document generation.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Allows callers to tune ASP.NET Core OpenAPI options.</summary>
    public Action<OpenApiOptions>? ConfigureOpenApi { get; set; }
}

/// <summary>OpenTelemetry defaults.</summary>
[PublicAPI]
public sealed class HeadlessApiOpenTelemetryOptions
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
}

/// <summary>HttpClient defaults.</summary>
[PublicAPI]
public sealed class HeadlessApiHttpClientOptions
{
    /// <summary>Whether to add the standard resilience handler to default HttpClient builders.</summary>
    public bool UseStandardResilienceHandler { get; set; } = true;

    /// <summary>Whether to register service discovery and enable it for default HttpClient builders.</summary>
    public bool UseServiceDiscovery { get; set; } = true;

    /// <summary>Whether to add a default User-Agent header based on the host application name.</summary>
    public bool AddApplicationUserAgent { get; set; } = true;
}
