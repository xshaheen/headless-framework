// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Headless.Messaging.Configuration;
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
        services.AddSingleton<MessagingMetricsEventListener>();
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
