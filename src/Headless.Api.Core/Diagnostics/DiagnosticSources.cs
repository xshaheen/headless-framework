// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Diagnostics;

/// <summary>
/// Well-known diagnostic event names emitted by Kestrel and
/// <see cref="Microsoft.AspNetCore.MiddlewareAnalysis.AnalysisStartupFilter"/>.
/// Use these constants when subscribing via <c>DiagnosticListener.SubscribeWithAdapter</c> to avoid
/// hard-coded string literals drifting from the platform values.
/// </summary>
public static class DiagnosticSources
{
    /// <summary>Fired by Kestrel when it rejects a malformed HTTP request.</summary>
    public const string KestrelOnBadRequest = "Microsoft.AspNetCore.Server.Kestrel.BadRequest";

    /// <summary>Fired by the middleware analysis pipeline just before a middleware component begins processing.</summary>
    public const string AnalysisOnMiddlewareStarting = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareStarting";

    /// <summary>Fired by the middleware analysis pipeline when a middleware component throws an unhandled exception.</summary>
    public const string AnalysisOnMiddlewareException = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareException";

    /// <summary>Fired by the middleware analysis pipeline after a middleware component completes (with or without an exception).</summary>
    public const string AnalysisOnMiddlewareFinished = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareFinished";
}
