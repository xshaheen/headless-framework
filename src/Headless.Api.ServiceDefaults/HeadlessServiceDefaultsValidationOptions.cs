// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

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

    /// <summary>Whether startup should fail when <c>UseStatusCodesRewriter()</c> was not applied.</summary>
    public bool RequireStatusCodesRewriter { get; set; } = true;
}
