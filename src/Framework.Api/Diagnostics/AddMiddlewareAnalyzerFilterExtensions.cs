// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.MiddlewareAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Diagnostics;

[PublicAPI]
public static class AddMiddlewareAnalyzerFilterExtensions
{
    public static void AddMiddlewareAnalyzerFilter(this WebApplicationBuilder builder)
    {
        builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, AnalysisStartupFilter>());
    }
}
