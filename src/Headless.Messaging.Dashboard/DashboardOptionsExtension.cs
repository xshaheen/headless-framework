// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
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

        // Register dashboard authentication (named AuthConfig + keyed IAuthService) through the shared extension
        // under this dashboard's own name, so another dashboard in the host keeps its own mode. Copy the values
        // already materialized on the fluent builder's AuthConfig.
        var auth = Builder.Auth;
        services.AddDashboardAuthentication(
            MessagingDashboardOptionsBuilder.AuthenticationName,
            config =>
            {
                config.Mode = auth.Mode;
                config.BasicCredentials = auth.BasicCredentials;
                config.ApiKey = auth.ApiKey;
                config.Host = auth.Host;
            }
        );

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

        // The dashboard endpoints name this policy only when SetCorsOrigins or SetCorsPolicy configured one, because the
        // host's pipeline runs UseCors only if the host adds it, and an endpoint with CORS metadata and no CORS
        // middleware throws at request time.
        if (Builder.CorsPolicyBuilder is { } corsPolicyBuilder)
        {
            services.AddCors(options =>
                options.AddPolicy(MessagingDashboardEndpoints.CorsPolicyName, corsPolicyBuilder)
            );
        }

        services.AddTransient<IStartupFilter>(_ => new MessagingDashboardStartupFilter(Builder));
    }
}

/// <summary>
/// Fails the host's start when the dashboard is registered but never mapped, so a missing
/// <c>MapHeadlessMessagingDashboard()</c> call is not a dashboard that silently answers 404.
/// </summary>
internal sealed class MessagingDashboardStartupFilter(MessagingDashboardOptionsBuilder config) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            // The host has mapped its endpoints by now: WebApplication maps them before it builds the pipeline, and a
            // Startup class maps them inside next(app).
            if (!config.EndpointMounted)
            {
                throw new InvalidOperationException(
                    "The Messaging dashboard is registered through UseDashboard(...) but not mapped. Call "
                        + "app.MapHeadlessMessagingDashboard() on the host's endpoints."
                );
            }
        };
    }
}

public static class MessagingOptionsExtensions
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers the Messaging Dashboard UI: its authentication, CORS policy, and services. Mount it on the host's
        /// endpoints with <c>app.MapHeadlessMessagingDashboard()</c>, at the path configured via <c>SetBasePath</c>
        /// (default: <c>/messaging/dashboard</c>); a host that registers it without mapping it fails to start.
        /// </summary>
        /// <param name="configure">
        /// An action to configure the dashboard options. An authentication mode must be chosen explicitly
        /// (<c>WithBasicAuth</c>, <c>WithApiKey</c>, <c>WithHostAuthentication</c>, or an
        /// explicit <c>WithNoAuth()</c> opt-out) or the host fails to start.
        /// </param>
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
