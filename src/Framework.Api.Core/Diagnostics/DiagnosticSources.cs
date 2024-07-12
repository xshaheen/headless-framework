namespace Framework.Api.Core.Diagnostics;

public static class DiagnosticSources
{
    public const string KestrelOnBadRequest = "Microsoft.AspNetCore.Server.Kestrel.BadRequest";

    public const string MiddlewareAnalysisOnMiddlewareStarting =
        "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareStarting";
    public const string MiddlewareAnalysisOnMiddlewareException =
        "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareException";
    public const string MiddlewareAnalysisOnMiddlewareFinished =
        "Microsoft.AspNetCore.MiddlewareAnalysis.MiddlewareFinished";
}
