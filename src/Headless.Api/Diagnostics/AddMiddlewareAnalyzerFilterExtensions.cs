// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.MiddlewareAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Diagnostics;

[PublicAPI]
public static class AddMiddlewareAnalyzerFilterExtensions
{
    public static void AddMiddlewareAnalyzerFilter(this IServiceCollection services)
    {
        services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, AnalysisStartupFilter>());
    }
}
