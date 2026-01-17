using Framework.Ticker.DependencyInjection;
using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Ticker.Dashboard.DependencyInjection;

/// <summary>
/// ASP.NET Core specific extensions for TickerQ with Dashboard support
/// </summary>
public static class AspNetCoreExtensions
{
    /// <summary>
    /// Initializes TickerQ for ASP.NET Core applications with Dashboard support
    /// </summary>
    public static IApplicationBuilder UseTickerQ(
        this IApplicationBuilder app,
        TickerQStartMode qStartMode = TickerQStartMode.Immediate
    )
    {
        var serviceProvider = app.ApplicationServices;

        // Initialize core TickerQ functionality using the base extension from TickerQ package
        serviceProvider.UseTickerQ(qStartMode);

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
