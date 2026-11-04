// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Starts a <see cref="WebApplication"/> on a test server that registers the Messaging dashboard through
/// <c>UseDashboard(...)</c> over in-memory messaging and mounts it on the host's endpoints, as a consumer host does.
/// </summary>
internal static class MessagingDashboardHost
{
    /// <summary>Builds and starts the host.</summary>
    /// <param name="configureDashboard">Configures the dashboard registered through <c>UseDashboard(...)</c>.</param>
    /// <param name="configureHost">Adds the host's own services, such as its authentication schemes.</param>
    /// <param name="configurePipeline">Adds the host's middleware and endpoints before the dashboard is mapped.</param>
    /// <param name="mapDashboard">
    /// <see langword="false"/> leaves out <c>MapHeadlessMessagingDashboard()</c>, which a host that registers the
    /// dashboard must call.
    /// </param>
    /// <param name="cancellationToken">Cancels the host's start.</param>
    public static async Task<WebApplication> StartAsync(
        Action<MessagingDashboardOptionsBuilder> configureDashboard,
        Action<WebApplicationBuilder>? configureHost = null,
        Action<WebApplication>? configurePipeline = null,
        bool mapDashboard = true,
        CancellationToken cancellationToken = default
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        configureHost?.Invoke(builder);

        builder.Services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.UseDashboard(configureDashboard);
        });

        var app = builder.Build();

        try
        {
            configurePipeline?.Invoke(app);

            if (mapDashboard)
            {
                app.MapHeadlessMessagingDashboard();
            }

            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    /// <summary>
    /// Registers the dashboard's authentication the way <c>UseDashboard(...)</c> does, for a test host that maps the
    /// dashboard endpoints itself: the keyed <c>IAuthService</c> and the authentication scheme and authorization policy
    /// named <see cref="MessagingDashboardOptionsBuilder.AuthenticationName"/>.
    /// </summary>
    public static IServiceCollection AddDashboardAuthentication(
        IServiceCollection services,
        MessagingDashboardOptionsBuilder config
    )
    {
        return services.AddDashboardAuthentication(
            MessagingDashboardOptionsBuilder.AuthenticationName,
            auth =>
            {
                auth.Mode = config.Auth.Mode;
                auth.BasicCredentials = config.Auth.BasicCredentials;
                auth.ApiKey = config.Auth.ApiKey;
                auth.Host = config.Auth.Host;
            }
        );
    }
}
