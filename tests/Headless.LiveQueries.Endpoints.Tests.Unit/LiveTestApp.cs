// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless;
using Headless.Api.ServiceDefaults;
using Headless.LiveQueries;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// An in-memory host with live endpoints, the Headless service defaults, and an authentication scheme whose users
/// a test changes while streams are open.
/// </summary>
internal sealed class LiveTestApp : IAsyncDisposable
{
    private LiveTestApp(WebApplication app, TestUsers users, TestMeterFactory meters)
    {
        App = app;
        Users = users;
        Meters = meters;
        Client = app.GetTestClient();
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public TestUsers Users { get; }

    public TestMeterFactory Meters { get; }

    public ILiveQueryInvalidator Invalidator => App.Services.GetRequiredService<ILiveQueryInvalidator>();

    /// <summary>Starts a host.</summary>
    /// <param name="map">Maps the endpoints.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <param name="configureLive">Changes the live-endpoint options.</param>
    /// <param name="configureSetup">Adds to the live-queries setup, after live endpoints are registered.</param>
    /// <param name="configureBuilder">Adds services.</param>
    /// <param name="beforeAuthentication">Adds middleware between routing and authentication.</param>
    /// <param name="afterAuthentication">Adds middleware between authentication and authorization.</param>
    /// <param name="useHeadless">Whether the pipeline starts with the framework's fixed outer block.</param>
    public static async Task<LiveTestApp> StartAsync(
        Action<WebApplication> map,
        CancellationToken cancellationToken,
        Action<LiveEndpointsOptions>? configureLive = null,
        Action<HeadlessLiveQueriesSetupBuilder>? configureSetup = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<WebApplication>? beforeAuthentication = null,
        Action<WebApplication>? afterAuthentication = null,
        bool useHeadless = false
    )
    {
        var users = new TestUsers();
        var meters = new TestMeterFactory();

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseTestServer();
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.Enabled = false;
            options.OpenApi.Enabled = false;
        });
        builder.Services.AddSingleton(users);
        builder.Services.AddSingleton<IMeterFactory>(meters);
        builder
            .Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { }
            );
        builder.Services.AddAuthorization();
        builder.Services.AddHeadlessLiveQueries(setup =>
        {
            // No wait between runs unless a test asks for one, so no test rides on the wall clock.
            setup.UseEndpoints(options =>
            {
                options.MinRerunInterval = TimeSpan.Zero;
                configureLive?.Invoke(options);
            });
            configureSetup?.Invoke(setup);
        });
        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        if (useHeadless)
        {
            app.UseHeadless();
        }

        app.UseRouting();
        beforeAuthentication?.Invoke(app);
        app.UseAuthentication();
        afterAuthentication?.Invoke(app);
        app.UseAuthorization();
        map(app);

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            meters.Dispose();

            throw;
        }

        return new LiveTestApp(app, users, meters);
    }

    /// <summary>Opens a stream on <paramref name="url" /> and returns once the response has started.</summary>
    public async Task<LiveStreamClient> OpenAsync(
        string url,
        CancellationToken cancellationToken,
        string? user = null,
        string accept = "text/event-stream",
        Action<HttpRequestMessage>? configure = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        if (user is not null)
        {
            request.Headers.Add(TestAuthenticationHandler.UserHeader, user);
        }

        configure?.Invoke(request);
        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        return new LiveStreamClient(response, await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    public ValueTask InvalidateAsync(string key, CancellationToken cancellationToken)
    {
        return Invalidator.InvalidateAsync([key], cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
        Meters.Dispose();
    }
}
