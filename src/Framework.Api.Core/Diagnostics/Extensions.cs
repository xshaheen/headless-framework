using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.MiddlewareAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Core.Diagnostics;

public static class Extensions
{
    public static void AddMiddlewareAnalyzerFilter(this WebApplicationBuilder builder)
    {
        builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, AnalysisStartupFilter>());
    }
}
