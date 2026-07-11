// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

internal sealed class DashboardOptionsExtension(Action<MessagingDashboardOptionsBuilder>? configure)
    : IMessagesOptionsExtension
{
    internal MessagingDashboardOptionsBuilder Builder { get; } = new();

    public void AddServices(IServiceCollection services)
    {
        configure?.Invoke(Builder);
        Builder.Validate();

        services.AddSingleton(Builder);

        // Register dashboard authentication (AuthConfig value + scoped IAuthService) through the
        // shared extension, copying the values already materialized on the fluent builder's AuthConfig.
        var auth = Builder.Auth;
        services.AddDashboardAuthentication(config =>
        {
            config.Mode = auth.Mode;
            config.BasicCredentials = auth.BasicCredentials;
            config.ApiKey = auth.ApiKey;
            config.CustomValidator = auth.CustomValidator;
            config.SessionTimeoutMinutes = auth.SessionTimeoutMinutes;
            config.HostAuthorizationPolicy = auth.HostAuthorizationPolicy;
        });

        services.AddSingleton<MessagingMetricsEventListener>();
        services.AddMemoryCache();

        services.AddRouting();
        services.AddAuthorization();

        services.AddCors(options =>
        {
            if (Builder.CorsPolicyBuilder is not null)
            {
                options.AddPolicy("HeadlessMessagingDashboardCORS", Builder.CorsPolicyBuilder);
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
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Enables the Messaging Dashboard UI, wiring up static-file serving, authentication, CORS,
        /// and all dashboard API endpoints. The dashboard is mounted at the path configured via
        /// <c>SetBasePath</c> (default: <c>/messaging</c>).
        /// </summary>
        /// <param name="configure">An action to configure the dashboard options.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseDashboard(Action<MessagingDashboardOptionsBuilder> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new DashboardOptionsExtension(configure));

            return setup;
        }
    }
}
