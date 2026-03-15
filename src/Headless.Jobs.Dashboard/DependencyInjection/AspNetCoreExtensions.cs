using Headless.Jobs.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.DependencyInjection;

/// <summary>
/// ASP.NET Core specific extensions for Jobs with Dashboard support
/// </summary>
public static class AspNetCoreExtensions
{
    /// <summary>
    /// Initializes Jobs for ASP.NET Core applications with Dashboard support
    /// </summary>
    public static IApplicationBuilder UseJobs(
        this IApplicationBuilder app,
        JobsStartMode qStartMode = JobsStartMode.Immediate
    )
    {
        var serviceProvider = app.ApplicationServices;

        // Initialize core Jobs functionality using the base extension from Jobs package
        serviceProvider.UseJobs(qStartMode);

        // Handle Dashboard-specific initialization if configured
        var tickerExecutionContext = serviceProvider.GetService<TickerExecutionContext>();
        if (tickerExecutionContext?.DashboardApplicationAction != null)
        {
            // Cast object back to IApplicationBuilder for Dashboard middleware
            tickerExecutionContext.DashboardApplicationAction(app);
            tickerExecutionContext.DashboardApplicationAction = null;
        }

        return app;
    }
}
