// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Jobs;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Security;

/// <summary>
/// One host runs the Jobs and Messaging dashboards, with different auth modes or both under host authentication. Each
/// dashboard's API and the Jobs live-update hub must accept that dashboard's own credential or session and refuse the
/// other's.
/// </summary>
public sealed class CrossDashboardAuthenticationTests : TestBase
{
    private const string _JobsApiKey = "jobs-key";
    private const string _MessagingUser = "ops";
    private const string _MessagingPassword = "messaging-pass";
    private const string _HostPolicy = "Operators";
    private const string _JobsBase = "/jobs/dashboard";
    private const string _MessagingBase = "/messaging/dashboard";
    private const string _JobsApi = "/jobs/dashboard/api/options";
    private const string _MessagingApi = "/messaging/dashboard/api/stats";
    private const string _JobsNegotiate = "/jobs/dashboard/job-notification-hub/negotiate?negotiateVersion=1";

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

        (await _GetAsync(client, "/messaging/dashboard/api/stats", authorization: null))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _GetAsync(client, "/messaging/dashboard/api/stats", $"Basic {_MessagingBasic}"))
            .Should()
            .Be(HttpStatusCode.OK);
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

        (await _GetAsync(client, "/messaging/dashboard/api/stats", $"Basic {_MessagingBasic}"))
            .Should()
            .Be(HttpStatusCode.OK);
        (await _GetAsync(client, "/messaging/dashboard/api/stats", $"Bearer {_JobsApiKey}"))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _PostAsync(client, "/messaging/dashboard/api/auth/validate", $"Bearer {_JobsApiKey}"))
            .Should()
            .Be(HttpStatusCode.Unauthorized);

        // The SPA's API-key hub credential is "Bearer:<key>"; a raw key is accepted as well.
        (await _HubStatusAsync(app, $"Bearer:{_JobsApiKey}"))
            .Should()
            .Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, _JobsApiKey)).Should().Be(HubOutcome.Authenticated);
        // A wrong or missing credential is refused before SignalR opens a connection that could hear a broadcast.
        (await _HubStatusAsync(app, _MessagingBasic))
            .Should()
            .Be(HubOutcome.Refused);
        (await _HubStatusAsync(app, accessToken: null)).Should().Be(HubOutcome.Refused);
    }

    [Fact]
    public async Task jobs_hub_accepts_the_basic_credential_the_spa_sends_as_its_access_token()
    {
        await using var app = await _StartAsync(jobs => jobs.WithBasicAuth("jobs-user", "jobs-pass"));

        (await _HubStatusAsync(app, "jobs-user:jobs-pass".ToBase64())).Should().Be(HubOutcome.Authenticated);
        (await _HubStatusAsync(app, _MessagingBasic)).Should().Be(HubOutcome.Refused);
    }

    [Fact]
    public async Task jobs_hub_negotiates_under_host_auth_only_with_a_jobs_session()
    {
        // given - both dashboards under host authentication in one host
        await using var app = await _StartHostAuthAsync();
        using var client = MessagingDashboardHost.CreateClient(app);
        var jobsSession = await _SessionAsync(client, _JobsBase, "Bearer operator");

        // then - the session cookie opens the hub; the host's bearer token does not, in a header or the query
        (await _PostAsync(client, _JobsNegotiate, authorization: null, cookie: jobsSession))
            .Should()
            .Be(HttpStatusCode.OK);
        (await _PostAsync(client, _JobsNegotiate, authorization: null)).Should().Be(HttpStatusCode.Unauthorized);
        (await _PostAsync(client, _JobsNegotiate, "Bearer operator")).Should().Be(HttpStatusCode.Unauthorized);
        (
            await _PostAsync(
                client,
                $"{_JobsNegotiate}&access_token={UrlEncoder.Default.Encode("Bearer operator")}",
                authorization: null
            )
        )
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _HubStatusAsync(app, "Bearer operator")).Should().Be(HubOutcome.Refused);
    }

    [Fact]
    public async Task each_dashboard_session_opens_only_its_own_dashboard()
    {
        // given - both dashboards under host authentication in one host, one session each
        await using var app = await _StartHostAuthAsync();
        using var client = MessagingDashboardHost.CreateClient(app);
        var jobsSession = await _SessionAsync(client, _JobsBase, "Bearer operator");
        var messagingSession = await _SessionAsync(client, _MessagingBase, "Bearer operator");

        // then - each session opens its own dashboard
        (await _GetAsync(client, _JobsApi, authorization: null, cookie: jobsSession))
            .Should()
            .Be(HttpStatusCode.OK);
        (await _GetAsync(client, _MessagingApi, authorization: null, cookie: messagingSession))
            .Should()
            .Be(HttpStatusCode.OK);

        // and - neither opens the other dashboard's API or the Jobs hub
        (await _GetAsync(client, _MessagingApi, authorization: null, cookie: jobsSession))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _GetAsync(client, _JobsApi, authorization: null, cookie: messagingSession))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _PostAsync(client, _JobsNegotiate, authorization: null, cookie: messagingSession))
            .Should()
            .Be(HttpStatusCode.Unauthorized);

        // and - one dashboard's ticket under the other's cookie name is protected for another scheme, so it is refused
        (await _GetAsync(client, _MessagingApi, authorization: null, cookie: _Rename(jobsSession, messagingSession)))
            .Should()
            .Be(HttpStatusCode.Unauthorized);
        (await _GetAsync(client, _JobsApi, authorization: null, cookie: _Rename(messagingSession, jobsSession)))
            .Should()
            .Be(HttpStatusCode.Unauthorized);

        // and - a browser sends both cookies on every request, and each dashboard still reads only its own
        var both = $"{jobsSession}; {messagingSession}";
        (await _GetAsync(client, _JobsApi, authorization: null, cookie: both)).Should().Be(HttpStatusCode.OK);
        (await _GetAsync(client, _MessagingApi, authorization: null, cookie: both)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task a_host_user_the_view_policy_refuses_gets_no_session_from_either_dashboard()
    {
        await using var app = await _StartHostAuthAsync();
        using var client = MessagingDashboardHost.CreateClient(app);

        (await MessagingDashboardHost.SignInAsync(client, "Bearer viewer", _JobsBase, AbortToken))
            .Should()
            .Be(HttpStatusCode.Forbidden);
        (await MessagingDashboardHost.SignInAsync(client, "Bearer viewer", _MessagingBase, AbortToken))
            .Should()
            .Be(HttpStatusCode.Forbidden);
    }

    private static Task<WebApplication> _StartHostAuthAsync()
    {
        return _StartAsync(
            jobs => jobs.WithHostAuthentication(host => host.ViewPolicy = _HostPolicy),
            dashboard => dashboard.WithHostAuthentication(host => host.ViewPolicy = _HostPolicy),
            configureHost: services =>
            {
                // The token names the host user's role.
                services.AddTestBearer(token => new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, token), new Claim(ClaimTypes.Role, token)],
                        MessagingDashboardHost.TestBearerScheme
                    )
                ));
                services.AddAuthorizationBuilder().AddPolicy(_HostPolicy, policy => policy.RequireRole("operator"));
            }
        );
    }

    /// <summary>
    /// Exchanges <paramref name="authorization"/> for the session of the dashboard at <paramref name="basePath"/> and
    /// returns its cookie as a request header value, so each request names the sessions it carries.
    /// </summary>
    private static async Task<string> _SessionAsync(HttpClient client, string basePath, string authorization)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{basePath}/api/auth/session", UriKind.Relative)
        );
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.SendAsync(request, AbortToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    /// <summary>The value of the <paramref name="session"/> cookie under the name of <paramref name="other"/>.</summary>
    private static string _Rename(string session, string other)
    {
        return $"{other[..other.IndexOf('=', StringComparison.Ordinal)]}={session[(session.IndexOf('=', StringComparison.Ordinal) + 1)..]}";
    }

    private static Task<WebApplication> _StartAsync(
        Action<DashboardOptionsBuilder> configureJobs,
        Action<MessagingDashboardOptionsBuilder>? configureMessaging = null,
        Action<IServiceCollection>? configureHost = null
    )
    {
        return MessagingDashboardHost.StartAsync(
            configureMessaging ?? (dashboard => dashboard.WithBasicAuth(_MessagingUser, _MessagingPassword)),
            configureHost: builder =>
            {
                configureHost?.Invoke(builder.Services);
                builder.Services.AddHeadlessJobs(options =>
                    options.DisableBackgroundServices().AddDashboard(configureJobs)
                );
            },
            configurePipeline: app =>
            {
                if (configureHost is not null)
                {
                    // After UseAuthentication: a WebApplication adds UseAuthorization ahead of the host's own middleware
                    // when the host does not call it, which would authorize before the host signs anyone in.
                    app.UseAuthentication();
                    app.UseAuthorization();
                }

                app.MapHeadlessJobsDashboard();
            },
            cancellationToken: AbortToken
        );
    }

    private static async Task<HttpStatusCode> _GetAsync(
        HttpClient client,
        string path,
        string? authorization,
        string? cookie = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        _SetHeaders(request, authorization, cookie);
        using var response = await client.SendAsync(request, AbortToken);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> _PostAsync(
        HttpClient client,
        string path,
        string? authorization,
        string? cookie = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        _SetHeaders(request, authorization, cookie);
        using var response = await client.SendAsync(request, AbortToken);

        return response.StatusCode;
    }

    private static void _SetHeaders(HttpRequestMessage request, string? authorization, string? cookie)
    {
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
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
            // TestServer reports a refused upgrade (401 from the dashboard's scheme, 401/403 from host authorization)
            // as a failed connect.
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
        await using var payload = new MemoryStream();
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

        /// <summary>Authentication or authorization refused the WebSocket upgrade.</summary>
        Refused = 2,
    }
}
