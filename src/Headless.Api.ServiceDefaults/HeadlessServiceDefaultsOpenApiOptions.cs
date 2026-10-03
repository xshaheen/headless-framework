// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>OpenAPI service-registration defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsOpenApiOptions
{
    /// <summary>Whether to register OpenAPI document generation.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Surface documents to publish. Null infers registered surfaces; empty selects the ordinary v1 document.</summary>
    public IReadOnlyCollection<string>? SurfaceDocumentNames { get; set; }

    /// <summary>Allows callers to tune ASP.NET Core OpenAPI options.</summary>
    public Action<OpenApiOptions>? ConfigureOpenApi { get; set; }

    /// <summary>The route pattern for OpenAPI JSON documents.</summary>
    [StringSyntax("Route")]
    public string RoutePattern { get; set; } = "/openapi/{documentName}.json";

    /// <summary>Whether to attach output-cache metadata to OpenAPI document endpoints.</summary>
    public bool CacheDocument { get; set; } = true;
}
