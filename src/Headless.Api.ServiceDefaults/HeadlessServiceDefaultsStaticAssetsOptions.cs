// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>Static web asset defaults.</summary>
[PublicAPI]
public sealed class HeadlessServiceDefaultsStaticAssetsOptions
{
    /// <summary>Whether to map static web assets when the generated manifest exists.</summary>
    public bool Enabled { get; set; } = true;
}
