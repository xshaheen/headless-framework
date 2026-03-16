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
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddDashboard<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        Action<DashboardOptionsBuilder>? configureDashboard = null
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var dashboardConfig = new DashboardOptionsBuilder
        {
            CorsPolicyBuilder = cors => cors.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowCredentials(),
        };

        configureDashboard?.Invoke(dashboardConfig);

        jobsConfiguration.DashboardServiceAction = (services) =>
        {
            services.AddScoped<
                IJobsDashboardRepository<TTimeJob, TCronJob>,
                JobsDashboardRepository<TTimeJob, TCronJob>
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

            services.AddDashboardService<TTimeJob, TCronJob>(dashboardConfig);
            services.AddSingleton<DashboardOptionsBuilder>(_ => dashboardConfig);
        };

        jobsConfiguration._UseDashboardDelegate(dashboardConfig);

        return jobsConfiguration;
    }

    private static void _UseDashboardDelegate<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration,
        DashboardOptionsBuilder dashboardConfig
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        jobsConfiguration.UseDashboardApplication(
            (appObj) =>
            {
                // Configure static files and middleware with endpoints
                var app = (Microsoft.AspNetCore.Builder.IApplicationBuilder)appObj;
                app.UseDashboardWithEndpoints<TTimeJob, TCronJob>(dashboardConfig);
            }
        );
    }
}
