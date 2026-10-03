// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>Options for configuring Headless API service defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsOptions
{
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
