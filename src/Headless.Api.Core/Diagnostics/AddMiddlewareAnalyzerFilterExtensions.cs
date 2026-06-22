// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.MiddlewareAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Diagnostics;

[PublicAPI]
public static class AddMiddlewareAnalyzerFilterExtensions
{
    /// <summary>
    /// Inserts <see cref="Microsoft.AspNetCore.MiddlewareAnalysis.AnalysisStartupFilter"/> at
    /// position 0 of the startup filter pipeline so every middleware is wrapped with
    /// <see cref="DiagnosticSources"/> events before any other filter runs.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static void AddMiddlewareAnalyzerFilter(this IServiceCollection services)
    {
        services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, AnalysisStartupFilter>());
    }
}
