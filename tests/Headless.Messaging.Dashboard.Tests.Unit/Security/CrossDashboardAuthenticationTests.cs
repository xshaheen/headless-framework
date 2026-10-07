// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Security;

/// <summary>
/// One host runs the Jobs and Messaging dashboards with different auth modes. Each dashboard's API and the Jobs
/// live-update hub must accept that dashboard's own credential and refuse the other's.
/// </summary>
public sealed class CrossDashboardAuthenticationTests : TestBase
{
    private const string _JobsApiKey = "jobs-key";
    private const string _MessagingUser = "ops";
    private const string _MessagingPassword = "messaging-pass";

    private static readonly string _MessagingBasic = $"{_MessagingUser}:{_MessagingPassword}".ToBase64();

    [Fact]
    public async Task jobs_without_auth_keeps_its_hub_and_api_open_while_messaging_requires_basic()
    {
        await using var app = await _StartAsync(jobs => jobs.WithNoAuth());
        using var client = app.GetTestClient();

        (await _HubStatusAsync(app, accessToken: null)).Should().Be(HubOutcome.Authenticated);
        (await client.GetAsync(new Uri("/jobs/dashboard/api/options", UriKind.Relative), AbortToken))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);

        (await _GetAsync(client, "/messaging/api/stats", authorization: null)).Should().Be(HttpStatusCode.Unauthorized);
        (await _GetAsync(client, "/messaging/api/stats", $"Basic {_MessagingBasic}")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task each_dashboard_accepts_only_its_own_credential()
    {
        await using var app = await _StartAsync(jobs => jobs.WithApiKey(_JobsApiKey));
        using var client = app.GetTestClient();

        (await _GetAsync(client, "/jobs/dashboard/api/options", $"Bearer {_JobsApiKey}"))
            .Should()
            .Be(HttpStatusCode.OK);
        (await _GetAsync(client, "/jobs/dashboard/api/options", $"Basic {_MessagingBasic}"))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _PostAsync(client, "/jobs/dashboard/api/auth/validate", $"Bearer {_JobsApiKey}"))
            .Should()
            .Be(HttpStatusCode.OK);

        (await _GetAsync(client, "/messaging/api/stats", $"Basic {_MessagingBasic}")).Should().Be(HttpStatusCode.OK);
        (await _GetAsync(client, "/messaging/api/stats", $"Bearer {_JobsApiKey}"))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _PostAsync(client, "/messaging/api/auth/validate", $"Bearer {_JobsApiKey}"))
            .Should()
            .Be(HttpStatusCode.Unauthorized);

        // The SPA's API-key hub credential is "Bearer:<key>"; a raw key is accepted as well.
        (await _HubStatusAsync(app, $"Bearer:{_JobsApiKey}"))
            .Should()
            .Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, _JobsApiKey)).Should().Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, _MessagingBasic)).Should().Be(HubOutcome.Closed);
        (await _HubStatusAsync(app, accessToken: null)).Should().Be(HubOutcome.Closed);
    }

    [Fact]
    public async Task jobs_hub_accepts_the_basic_credential_the_spa_sends_as_its_access_token()
    {
        await using var app = await _StartAsync(jobs => jobs.WithBasicAuth("jobs-user", "jobs-pass"));

        (await _HubStatusAsync(app, "jobs-user:jobs-pass".ToBase64())).Should().Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, _MessagingBasic)).Should().Be(HubOutcome.Closed);
    }

    [Fact]
    public async Task jobs_hub_accepts_a_custom_credential_verbatim()
    {
        await using var app = await _StartAsync(jobs =>
            jobs.WithCustomAuth((credential, _) => string.Equals(credential, "Token abc", StringComparison.Ordinal))
        );

        (await _HubStatusAsync(app, "Token abc")).Should().Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, "Token other")).Should().Be(HubOutcome.Closed);
    }

    [Fact]
    public async Task jobs_hub_under_host_auth_signs_in_the_access_token_and_enforces_the_host_policy()
    {
        await using var app = await _StartAsync(
            jobs => jobs.WithHostAuthentication(TokenAuthenticationHandler.Policy),
            configureHost: services =>
            {
                services
                    .AddAuthentication(TokenAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(
                        TokenAuthenticationHandler.SchemeName,
                        _ => { }
                    );
                services
                    .AddAuthorizationBuilder()
                    .AddPolicy(TokenAuthenticationHandler.Policy, policy => policy.RequireRole("operator"));
            }
        );

        (await _HubStatusAsync(app, "Token operator")).Should().Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, "Token viewer")).Should().Be(HubOutcome.Refused);
        (await _HubStatusAsync(app, accessToken: null)).Should().Be(HubOutcome.Refused);
    }

    private static async Task<WebApplication> _StartAsync(
        Action<DashboardOptionsBuilder> configureJobs,
        Action<IServiceCollection>? configureHost = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        configureHost?.Invoke(builder.Services);

        builder.Services.AddHeadlessJobs(options => options.DisableBackgroundServices().AddDashboard(configureJobs));
        builder.Services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.UseDashboard(dashboard => dashboard.WithBasicAuth(_MessagingUser, _MessagingPassword));
        });

        var app = builder.Build();
        if (configureHost is not null)
        {
            app.UseAuthentication();
        }

        await app.StartAsync(AbortToken);

        return app;
    }

    private static async Task<HttpStatusCode> _GetAsync(HttpClient client, string path, string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        _SetAuthorization(request, authorization);
        using var response = await client.SendAsync(request, AbortToken);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> _PostAsync(HttpClient client, string path, string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        _SetAuthorization(request, authorization);
        using var response = await client.SendAsync(request, AbortToken);

        return response.StatusCode;
    }

    private static void _SetAuthorization(HttpRequestMessage request, string? authorization)
    {
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
    }

    /// <summary>
    /// Connects to the Jobs hub over a real WebSocket, as the dashboard SPA does (WebSockets only, credential in
    /// <c>access_token</c>), completes the SignalR JSON handshake, and asks the hub for its status.
    /// </summary>
    private static async Task<HubOutcome> _HubStatusAsync(WebApplication app, string? accessToken)
    {
        var query = accessToken is null ? "" : $"?access_token={UrlEncoder.Default.Encode(accessToken)}";
        var uri = new Uri($"ws://localhost/jobs/dashboard/job-notification-hub{query}");
        var wsClient = app.GetTestServer().CreateWebSocketClient();

        WebSocket socket;
        try
        {
            socket = await wsClient.ConnectAsync(uri, AbortToken);
        }
        catch (InvalidOperationException)
        {
            // TestServer reports a refused upgrade (401/403 from authorization) as a failed connect.
            return HubOutcome.Refused;
        }

        using (socket)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            await _SendAsync(socket, """{"protocol":"json","version":1}""", timeout.Token);
            await _SendAsync(socket, """{"type":1,"target":"GetStatus","arguments":[]}""", timeout.Token);

            while (await _ReceiveRecordsAsync(socket, timeout.Token) is { } records)
            {
                foreach (var record in records)
                {
                    using var message = JsonDocument.Parse(record);
                    var root = message.RootElement;
                    if (root.TryGetProperty("type", out var type) && type.GetInt32() == 7)
                    {
                        return HubOutcome.Closed;
                    }

                    if (
                        root.TryGetProperty("target", out var target)
                        && string.Equals(target.GetString(), "Status", StringComparison.Ordinal)
                    )
                    {
                        return root.GetProperty("arguments")[0].GetProperty("authenticated").GetBoolean()
                            ? HubOutcome.Authenticated
                            : HubOutcome.Closed;
                    }
                }
            }

            return HubOutcome.Closed;
        }
    }

    private static Task _SendAsync(WebSocket socket, string json, CancellationToken cancellationToken)
    {
        return socket.SendAsync(
            Encoding.UTF8.GetBytes(json + '\u001e'),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken
        );
    }

    /// <summary>Reads one WebSocket message and splits it into SignalR records; <see langword="null"/> once closed.</summary>
    private static async Task<string[]?> _ReceiveRecordsAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var payload = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            payload.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(payload.ToArray()).Split('\u001e', StringSplitOptions.RemoveEmptyEntries);
    }

    private enum HubOutcome
    {
        /// <summary>The hub accepted the connection and reports it authenticated.</summary>
        Authenticated = 0,

        /// <summary>The hub completed the upgrade, then closed the connection.</summary>
        Closed = 1,

        /// <summary>Authorization refused the WebSocket upgrade.</summary>
        Refused = 2,
    }

    /// <summary>A host scheme that signs in <c>Authorization: Token &lt;role&gt;</c> as a user in that role.</summary>
    private sealed class TokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Token";
        public const string Policy = "Operators";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Token ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = header["Token ".Length..];
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)],
                SchemeName
            );

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
        }
    }
}
