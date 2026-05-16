// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Headless.Api;

/// <summary>Options for <see cref="ApiSetup.UseHeadless(WebApplication, Action{HeadlessApiDefaultsOptions}?)"/>.</summary>
public sealed class HeadlessApiDefaultsOptions
{
    internal const string AppliedKey = "Headless.Api.Defaults.Applied";

    /// <summary>Whether to run ASP.NET Core forwarded-headers middleware.</summary>
    public bool UseForwardedHeaders { get; set; } = true;

    /// <summary>
    /// Trusts forwarded headers from any proxy. Keep disabled unless the app is only reachable through trusted infrastructure.
    /// </summary>
    public bool TrustForwardedHeadersFromAnyProxy { get; set; }

    /// <summary>The forwarded headers to process.</summary>
    public ForwardedHeaders ForwardedHeaders { get; set; } =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

    /// <summary>Allows callers to tune ASP.NET Core forwarded-header options.</summary>
    public Action<ForwardedHeadersOptions>? ConfigureForwardedHeaders { get; set; }

    /// <summary>Whether to run response compression middleware.</summary>
    public bool UseResponseCompression { get; set; } = true;

    /// <summary>Whether to convert empty error status codes into ProblemDetails when possible.</summary>
    public bool UseStatusCodePages { get; set; } = true;

    /// <summary>Whether to run ASP.NET Core exception-handler middleware.</summary>
    public bool UseExceptionHandler { get; set; } = true;

    /// <summary>
    /// The error endpoint path used by ASP.NET Core's exception-handler middleware. Leave unset to use ProblemDetails directly.
    /// </summary>
    [StringSyntax("Route")]
    public string? ExceptionHandlerPath { get; set; }

    /// <summary>Whether exception-handler middleware should create a scope for errors when a path is configured.</summary>
    public bool CreateScopeForErrors { get; set; } = true;

    /// <summary>Whether to run HTTPS redirection middleware.</summary>
    public bool UseHttpsRedirection { get; set; } = true;

    /// <summary>Whether to run HSTS middleware outside Development.</summary>
    public bool UseHsts { get; set; } = true;

    /// <summary>Whether to add a no-cache header when the response did not set cache headers.</summary>
    public bool SetNoCacheWhenMissingCacheHeaders { get; set; } = true;

    /// <summary>Whether to run antiforgery middleware.</summary>
    public bool UseAntiforgery { get; set; } = true;
}
