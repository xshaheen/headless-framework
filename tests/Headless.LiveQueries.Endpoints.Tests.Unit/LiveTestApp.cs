// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless;
using Headless.Api.ServiceDefaults;
using Headless.LiveQueries;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// An in-memory host with live endpoints, the Headless service defaults, and an authentication scheme whose users
/// a test changes while streams are open.
/// </summary>
internal sealed class LiveTestApp : IAsyncDisposable
{
    // Spelled out here, not taken from the package: these names are the wire protocol a client is written against.
    public const string SessionHeader = "X-Headless-Live-Session";
    public const string CommandHeader = "X-Headless-Live-Command";
    public const string AcknowledgementHeader = "X-Headless-Live-Acknowledgement";

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
        app.UseHeadlessLiveCommands();
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
        Action<HttpRequestMessage>? configure = null,
        string? session = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        if (user is not null)
        {
            request.Headers.Add(TestAuthenticationHandler.UserHeader, user);
        }

        if (session is not null)
        {
            request.Headers.Add(SessionHeader, session);
        }

        configure?.Invoke(request);
        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        return new LiveStreamClient(response, await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    /// <summary>Posts to <paramref name="url" /> as a client that tags its command.</summary>
    public async Task<HttpResponseMessage> SendCommandAsync(
        string url,
        CancellationToken cancellationToken,
        string? user = null,
        string? session = null,
        string? command = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (user is not null)
        {
            request.Headers.Add(TestAuthenticationHandler.UserHeader, user);
        }

        if (session is not null)
        {
            request.Headers.Add(SessionHeader, session);
        }

        if (command is not null)
        {
            request.Headers.Add(CommandHeader, command);
        }

        return await Client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Posts to <paramref name="url" /> without an HTTP client in between, so a header can hold what a client
    /// would refuse to send, such as a line break. Returns the response headers.
    /// </summary>
    public async Task<IHeaderDictionary> SendRawCommandAsync(
        string url,
        string user,
        string session,
        string command,
        CancellationToken cancellationToken
    )
    {
        var context = await App.GetTestServer()
            .SendAsync(
                request =>
                {
                    request.Request.Method = HttpMethods.Post;
                    request.Request.Path = url;
                    request.Request.Headers[TestAuthenticationHandler.UserHeader] = user;
                    request.Request.Headers[SessionHeader] = session;
                    request.Request.Headers[CommandHeader] = command;
                },
                cancellationToken
            );

        return context.Response.Headers;
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
