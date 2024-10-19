// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Api.Diagnostics;

public static class DiagnosticSources
{
    public const string KestrelOnBadRequest = "Microsoft.AspNetCore.Server.Kestrel.BadRequest";
    public const string AnalysisOnMiddlewareStarting = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareStarting";
    public const string AnalysisOnMiddlewareException = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareException";
    public const string AnalysisOnMiddlewareFinished = "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareFinished";
}
