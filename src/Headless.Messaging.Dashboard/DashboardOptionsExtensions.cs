// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

internal sealed class DashboardOptionsExtension(Action<MessagingDashboardOptionsBuilder> configure)
    : IMessagesOptionsExtension
{
    internal MessagingDashboardOptionsBuilder Builder { get; } = new();

    public void AddServices(IServiceCollection services)
    {
        configure?.Invoke(Builder);
        Builder.Validate();

        services.AddSingleton(Builder);
        services.AddSingleton(Builder.Auth);
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<MessagingMetricsEventListener>();
        services.AddMemoryCache();

        services.AddRouting();
        services.AddAuthorization();

        services.AddCors(options =>
        {
            if (Builder.CorsPolicyBuilder is not null)
            {
                options.AddPolicy("Messaging_Dashboard_CORS", Builder.CorsPolicyBuilder);
            }
        });

        // If using host auth, ensure auth services exist
        if (Builder.Auth.Mode == AuthMode.Host)
        {
            var hasAuthService = services.Any(s =>
                s.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService)
                || string.Equals(s.ServiceType.Name, "IAuthenticationSchemeProvider", StringComparison.Ordinal)
            );

            if (!hasAuthService)
            {
                services.AddAuthentication();
                services.AddAuthorization();
            }
        }

        // Auto-inject middleware pipeline via IStartupFilter
        services.AddTransient<IStartupFilter>(_ => new MessagingDashboardStartupFilter(Builder));
    }
}

internal sealed class MessagingDashboardStartupFilter(MessagingDashboardOptionsBuilder config) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            // Use the new app.Map branching pipeline (matches Jobs pattern)
            app.UseMessagingDashboard(config);
        };
    }
}

public static class MessagingOptionsExtensions
{
    extension(MessagingOptions messagingOptions)
    {
        /// <summary>
        /// Enable the Messaging Dashboard with the new fluent builder API.
        /// </summary>
        public MessagingOptions UseDashboard(Action<MessagingDashboardOptionsBuilder> configure)
        {
            Argument.IsNotNull(configure);

            messagingOptions.RegisterExtension(new DashboardOptionsExtension(configure));

            return messagingOptions;
        }
    }
}
