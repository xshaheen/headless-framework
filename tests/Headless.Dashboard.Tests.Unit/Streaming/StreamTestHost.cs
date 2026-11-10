// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Streaming;

/// <summary>
/// A real host serving the dashboard without authentication at <c>/dash</c>, with the live stream added.
/// </summary>
internal static class StreamTestHost
{
    public const string BasePath = "/dash";

    public static async Task<WebApplication> StartAsync(
        FakeTimeProvider time,
        FakeChangeStore store,
        Action<HeadlessDashboardSetupBuilder> addModules,
        Action<IServiceCollection>? services,
        CancellationToken cancellationToken
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(store);
        services?.Invoke(builder.Services);

        builder.Services.AddHeadlessDashboard(dashboard => addModules(dashboard.WithNoAuth().SetBasePath(BasePath)));
        StreamingWiring.AddServices(builder.Services);

        var app = builder.Build();
        app.MapHeadlessDashboard();
        StreamingWiring.MapEndpoints(app);

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    /// <summary>Starts a host with the one module <c>diag</c>.</summary>
    public static Task<WebApplication> StartDiagAsync(
        FakeTimeProvider time,
        FakeChangeStore store,
        CancellationToken cancellationToken
    )
    {
        return StartAsync(
            time,
            store,
            dashboard => dashboard.AddModule<DiagStreamModule, DiagStreamModuleOptions>("diag"),
            services: null,
            cancellationToken
        );
    }

    /// <summary>The stream path for <paramref name="topics"/>.</summary>
    public static string StreamPath(string topics) => $"{BasePath}/api/stream?topics={Uri.EscapeDataString(topics)}";
}

/// <summary>
/// Adds the live stream to a test host on top of <c>AddHeadlessDashboard</c> and <c>MapHeadlessDashboard</c>, the way
/// the dashboard's own registration adds it.
/// </summary>
internal static class StreamingWiring
{
    public static void AddServices(IServiceCollection services) => DashboardStreamingSetup.AddStreaming(services);

    /// <summary>Maps the stream on the dashboard's group unless the dashboard's registration maps it.</summary>
    public static void MapEndpoints(WebApplication app)
    {
        var registration = app.Services.GetRequiredService<DashboardRegistration>();
        Action<RouteGroupBuilder> map = DashboardStreamingSetup.MapStreaming;

        if (!registration.EndpointMappers.Contains(map))
        {
            map(registration.Group!);
        }
    }
}
