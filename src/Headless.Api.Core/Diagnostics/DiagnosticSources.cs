// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Diagnostics;

public static class DiagnosticSources
{
    public const string KestrelOnBadRequest = "Microsoft.AspNetCore.Server.Kestrel.BadRequest";
    public const string AnalysisOnMiddlewareStarting = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareStarting";
    public const string AnalysisOnMiddlewareException = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareException";
    public const string AnalysisOnMiddlewareFinished = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareFinished";
}
