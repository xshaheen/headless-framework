// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Headless.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<MessagingDashboardCache>();
        services.TryAddSingleton<KeyedAsyncLock>();
        services
            .AddHttpClient(
                MessagingDashboardEndpoints.PingHttpClientName,
                static client => client.Timeout = TimeSpan.FromSeconds(5)
            )
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddRouting();
        services.AddAuthorization();

        // Always register the named policy so the CORS middleware (UseCors, unconditional in the pipeline)
        // can resolve it: the dashboard endpoints carry RequireCors metadata and would throw at request time
        // if the CORS middleware never ran. An unconfigured policy is empty (no origins) -> same-origin only.
        services.AddCors(options =>
            options.AddPolicy("HeadlessMessagingDashboardCORS", Builder.CorsPolicyBuilder ?? (static _ => { }))
        );

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
