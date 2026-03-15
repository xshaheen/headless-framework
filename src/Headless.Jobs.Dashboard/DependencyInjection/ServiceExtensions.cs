using Headless.Jobs.Authentication;
using Headless.Jobs.Entities;
using Headless.Jobs.Hubs;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Jobs.DependencyInjection;

public static class ServiceExtensions
{
    public static TickerOptionsBuilder<TTimeTicker, TCronTicker> AddDashboard<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        Action<DashboardOptionsBuilder>? configureDashboard = null
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        var dashboardConfig = new DashboardOptionsBuilder
        {
            CorsPolicyBuilder = cors => cors.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowCredentials(),
        };

        configureDashboard?.Invoke(dashboardConfig);

        tickerConfiguration.DashboardServiceAction = (services) =>
        {
            services.AddScoped<
                ITickerDashboardRepository<TTimeTicker, TCronTicker>,
                TickerDashboardRepository<TTimeTicker, TCronTicker>
            >();

            services.Replace(
                ServiceDescriptor.Singleton(
                    services.AddSingleton<IJobsNotificationHubSender, JobsNotificationHubSender>()
                )
            );

            // Validate configuration
            dashboardConfig.Validate();

            // Register authentication system
            services.AddSingleton(dashboardConfig.Auth);
            services.AddScoped<IAuthService, AuthService>();

            // Add authentication services if using host authentication
            if (dashboardConfig.Auth.Mode == AuthMode.Host)
            {
                // The host application should configure authentication services
                // We just ensure they're available
                var hasAuthenticationService = services.Any(s =>
                    s.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService)
                    || string.Equals(s.ServiceType.Name, "IAuthenticationSchemeProvider", StringComparison.Ordinal)
                );

                if (!hasAuthenticationService)
                {
                    services.AddAuthentication();
                    services.AddAuthorization();
                }
            }

            services.AddDashboardService<TTimeTicker, TCronTicker>(dashboardConfig);
            services.AddSingleton<DashboardOptionsBuilder>(_ => dashboardConfig);
        };

        tickerConfiguration._UseDashboardDelegate(dashboardConfig);

        return tickerConfiguration;
    }

    private static void _UseDashboardDelegate<TTimeTicker, TCronTicker>(
        this TickerOptionsBuilder<TTimeTicker, TCronTicker> tickerConfiguration,
        DashboardOptionsBuilder dashboardConfig
    )
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tickerConfiguration.UseDashboardApplication(
            (appObj) =>
            {
                // Configure static files and middleware with endpoints
                var app = (Microsoft.AspNetCore.Builder.IApplicationBuilder)appObj;
                app.UseDashboardWithEndpoints<TTimeTicker, TCronTicker>(dashboardConfig);
            }
        );
    }
}
