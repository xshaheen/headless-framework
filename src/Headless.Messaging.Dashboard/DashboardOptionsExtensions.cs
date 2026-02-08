// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.Authentication;
using Headless.Messaging.Dashboard.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

internal sealed class DashboardOptionsExtension(Action<DashboardOptions> option) : IMessagesOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var dashboardOptions = new DashboardOptions();
        option?.Invoke(dashboardOptions);
        services.AddTransient<IStartupFilter, MessagingDashboardStartupFilter>();
        services.AddSingleton(dashboardOptions);

        if (dashboardOptions.Auth.IsEnabled)
        {
            dashboardOptions.Auth.Validate();
            services.AddSingleton(dashboardOptions.Auth);
            services.AddSingleton<IAuthService, AuthService>();
        }

        services.AddSignalR();

        services.AddSingleton<MessagingMetricsEventListener>();
        services.AddScoped<ISchedulingDashboardRepository>(sp =>
        {
            var storage = sp.GetService<IScheduledJobStorage>();
            return storage is null ? null! : new SchedulingDashboardRepository(storage);
        });
    }
}

internal sealed class MessagingDashboardStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            app.UseMessagingDashboard();
        };
    }
}

public static class MessagingOptionsExtensions
{
    extension(MessagingOptions messagingOptions)
    {
        public MessagingOptions UseDashboard()
        {
            return messagingOptions.UseDashboard(_ => { });
        }

        public MessagingOptions UseDashboard(Action<DashboardOptions> options)
        {
            Argument.IsNotNull(options);

            messagingOptions.RegisterExtension(new DashboardOptionsExtension(options));

            return messagingOptions;
        }
    }
}
