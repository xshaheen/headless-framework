// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard.Authentication;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Starts a <see cref="WebApplication"/> on a test server that registers the Messaging dashboard through
/// <c>UseDashboard(...)</c> over in-memory messaging and mounts it on the host's endpoints, as a consumer host does.
/// </summary>
internal static class MessagingDashboardHost
{
    /// <summary>The header the dashboard SPA sends on every request, which every state-changing request needs.</summary>
    public const string RequestedWithHeader = "X-Requested-With";

    /// <summary>The scheme <see cref="AddTestBearer"/> registers.</summary>
    public const string TestBearerScheme = "TestBearer";

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

    /// <summary>
    /// A client for <paramref name="host"/> that calls it over <c>https</c>, as a browser reaches a dashboard whose
    /// session cookie is <c>Secure</c>, and sends <see cref="RequestedWithHeader"/> on every request, as the SPA does.
    /// </summary>
    public static HttpClient CreateClient(IHost host)
    {
        var client = host.GetTestClient();
        client.BaseAddress = new Uri("https://localhost/");
        client.DefaultRequestHeaders.Add(RequestedWithHeader, "XMLHttpRequest");

        return client;
    }

    /// <summary>
    /// Exchanges the host credential <paramref name="authorization"/> for a dashboard session through
    /// <c>{basePath}/api/auth/session</c>. On success the session cookie rides on every later request of
    /// <paramref name="client"/>, because the test server keeps no cookies of its own.
    /// </summary>
    /// <returns>The exchange's status: <c>204</c> when a session was issued.</returns>
    public static async Task<HttpStatusCode> SignInAsync(
        HttpClient client,
        string authorization,
        string basePath = "/messaging/dashboard",
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{basePath}/api/auth/session", UriKind.Relative)
        );
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            var cookies = response.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]);
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        }

        return response.StatusCode;
    }

    /// <summary>
    /// Registers <see cref="TestBearerScheme"/> as the host's default scheme: <c>Authorization: Bearer &lt;token&gt;</c>
    /// signs in the principal <paramref name="resolve"/> returns for the token, and nothing else signs anyone in.
    /// </summary>
    public static AuthenticationBuilder AddTestBearer(
        this IServiceCollection services,
        Func<string, ClaimsPrincipal?> resolve
    )
    {
        return services
            .AddAuthentication(TestBearerScheme)
            .AddScheme<TestBearerOptions, TestBearerHandler>(TestBearerScheme, options => options.Resolve = resolve);
    }

    internal sealed class TestBearerOptions : AuthenticationSchemeOptions
    {
        public Func<string, ClaimsPrincipal?> Resolve { get; set; } = static _ => null;
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<TestBearerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<TestBearerOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = Options.Resolve(header["Bearer ".Length..]);
            if (principal?.Identity is not { IsAuthenticated: true })
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
