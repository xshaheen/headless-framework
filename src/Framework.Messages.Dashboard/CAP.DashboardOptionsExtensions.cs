// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messages;

internal sealed class DashboardOptionsExtension(Action<DashboardOptions> option) : IMessagesOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var dashboardOptions = new DashboardOptions();
        option?.Invoke(dashboardOptions);
        services.AddTransient<IStartupFilter, CapStartupFilter>();
        services.AddSingleton(dashboardOptions);
        services.AddSingleton<CapMetricsEventListener>();
    }
}

internal sealed class CapStartupFilter : IStartupFilter
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
    public static MessagingOptions UseDashboard(this MessagingOptions capOptions)
    {
        return capOptions.UseDashboard(opt => { });
    }

    public static MessagingOptions UseDashboard(this MessagingOptions capOptions, Action<DashboardOptions> options)
    {
        Argument.IsNotNull(options);

        capOptions.RegisterExtension(new DashboardOptionsExtension(options));

        return capOptions;
    }
}
