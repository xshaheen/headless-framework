// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Net.Http.Headers;
using Tests.Modules.Fixtures;

namespace Tests.Authentication;

/// <summary>
/// The dashboard shell's session cookie on a real host: its name follows the host's application, and it lives beside
/// the Vue dashboards' sessions without touching them.
/// </summary>
public sealed partial class ShellSessionTests : TestBase
{
    private const string _LegacyName = "Headless.Jobs.Dashboard";

    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();

    [Fact]
    public async Task should_keep_a_separate_session_per_application_when_two_hosts_share_one_host_name()
    {
        await using var appA = await _StartHostModeAsync(applicationName: "app-a");
        await using var appB = await _StartHostModeAsync(applicationName: "app-b");
        using var clientA = ShellTestHost.Client(appA);
        using var clientB = ShellTestHost.Client(appB);

        using var signInA = await ShellTestHost.ExchangeAsync(clientA, "Bearer ops", AbortToken);
        using var signInB = await ShellTestHost.ExchangeAsync(clientB, "Bearer ops", AbortToken);
        var cookieA = ShellTestHost.SetCookie(signInA, "__Host-headless-dashboard-app-a");
        var cookieB = ShellTestHost.SetCookie(signInB, "__Host-headless-dashboard-app-b");

        cookieA.Should().NotBeNull();
        cookieB.Should().NotBeNull();

        // The browser holds both cookies for the one host name and sends both to each application.
        var both = ShellTestHost.Cookie(cookieA!) + "; " + ShellTestHost.Cookie(cookieB!);
        (await ShellTestHost.GetDiagAsync(clientA, both, AbortToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ShellTestHost.GetDiagAsync(clientB, both, AbortToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Each application reads only its own cookie.
        (await ShellTestHost.GetDiagAsync(clientB, ShellTestHost.Cookie(cookieA!), AbortToken))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await ShellTestHost.GetDiagAsync(clientA, ShellTestHost.Cookie(cookieB!), AbortToken))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_name_the_cookie_with_cookie_token_characters_when_the_application_name_has_spaces_and_dots()
    {
        await using var app = await _StartHostModeAsync(applicationName: "My App.Web");
        using var client = ShellTestHost.Client(app);

        using var signIn = await ShellTestHost.ExchangeAsync(client, "Bearer ops", AbortToken);

        signIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var name = _SessionCookieNames(signIn).Should().ContainSingle().Which;
        name.Should().Be("__Host-headless-dashboard-my-app-web");
        _CookieTokenRegex.IsMatch(name).Should().BeTrue();
    }

    [Fact]
    public async Task should_use_the_session_cookie_name_when_the_host_sets_one()
    {
        await using var app = await _StartHostModeAsync(host => host.SessionCookieName = "ops-session");
        using var client = ShellTestHost.Client(app);

        using var signIn = await ShellTestHost.ExchangeAsync(client, "Bearer ops", AbortToken);

        signIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShellTestHost.SetCookie(signIn, "ops-session").Should().NotBeNull();
    }

    [Fact]
    public async Task should_report_the_signed_in_host_user_on_auth_info_when_only_the_session_cookie_is_sent()
    {
        await using var app = await _StartHostModeAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);

        var anonymous = await ShellTestHost.InfoAsync(client, cookie: null, AbortToken);
        var signedIn = await ShellTestHost.InfoAsync(client, cookie, AbortToken);

        anonymous.GetProperty("mode").GetString().Should().Be("host");
        anonymous.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        signedIn.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        signedIn.GetProperty("name").GetString().Should().Be("ops");
        signedIn.GetProperty("canOperate").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(AuthMode.Host)]
    [InlineData(AuthMode.Basic)]
    [InlineData(AuthMode.ApiKey)]
    [InlineData(AuthMode.None)]
    public async Task should_answer_400_when_a_session_post_lacks_the_requested_with_header(AuthMode mode)
    {
        await using var app = await ShellTestHost.StartAsync(
            dashboard => _ChooseMode(dashboard, mode).AddModule<DiagEndpointModule, DiagModuleOptions>("diag"),
            new ShellTestHostOptions { Time = _time },
            AbortToken
        );
        using var client = ShellTestHost.Client(app);

        using var response = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Post,
            ShellTestHost.SessionPath,
            AbortToken,
            authorization: "Basic " + "ops:secret".ToBase64(),
            requestedWith: false
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _SessionCookieNames(response).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_the_vue_dashboard_cookie_name_and_session_apart_when_the_shell_is_registered_beside_it()
    {
        await using var app = await _StartHostModeAsync(
            configureServices: services =>
                services.AddDashboardAuthentication(_LegacyName, auth => auth.Mode = AuthMode.Host),
            mapHost: host =>
            {
                var legacy = host.MapGroup("/jobs");
                legacy.MapDashboardAuthEndpoints(_LegacyName);
                legacy
                    .MapGroup("/api")
                    .RequireDashboardAuthentication(_LegacyName)
                    .MapGet("/stats", (HttpContext context) => context.User.Identity?.Name);
            }
        );
        using var client = ShellTestHost.Client(app);

        using var legacyExchange = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Post,
            "/jobs/api/auth/session",
            AbortToken,
            authorization: "Bearer ops"
        );
        using var shellExchange = await ShellTestHost.ExchangeAsync(client, "Bearer ops", AbortToken);

        var legacyCookie = ShellTestHost.SetCookie(legacyExchange, "__Host-headless-jobs-dashboard");
        legacyCookie.Should().NotBeNull();
        ShellTestHost.SetCookie(legacyExchange, ShellTestHost.DefaultCookieName).Should().BeNull();
        var shellCookie = ShellTestHost.SetCookie(shellExchange, ShellTestHost.DefaultCookieName);
        shellCookie.Should().NotBeNull();
        var legacy = ShellTestHost.Cookie(legacyCookie!);
        var shell = ShellTestHost.Cookie(shellCookie!);

        (await _GetLegacyStatsAsync(client, legacy)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _GetLegacyStatsAsync(client, shell)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ShellTestHost.GetDiagAsync(client, shell, AbortToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ShellTestHost.GetDiagAsync(client, legacy, AbortToken))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_delete_the_shell_session_cookie_when_the_host_signs_out_without_naming_the_dashboard()
    {
        await using var app = await _StartHostModeAsync(mapHost: host =>
            host.MapPost("/host/logout", (HttpContext context) => context.SignOutHeadlessDashboardAsync())
        );
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);

        using var signOut = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Post,
            "/host/logout",
            AbortToken,
            cookie: cookie
        );

        signOut.StatusCode.Should().Be(HttpStatusCode.OK);
        ShellTestHost.IsDeletion(ShellTestHost.SetCookie(signOut, ShellTestHost.DefaultCookieName)).Should().BeTrue();
    }

    private Task<HttpResponseMessage> _GetLegacyStatsAsync(HttpClient client, string cookie)
    {
        return ShellTestHost.SendAsync(client, HttpMethod.Get, "/jobs/api/stats", AbortToken, cookie: cookie);
    }

    private Task<WebApplication> _StartHostModeAsync(
        Action<DashboardHostAuthenticationOptions>? configureHost = null,
        string? applicationName = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? mapHost = null
    )
    {
        return ShellTestHost.StartAsync(
            dashboard =>
                dashboard
                    .WithHostAuthentication(host => configureHost?.Invoke(host))
                    .AddModule<DiagEndpointModule, DiagModuleOptions>("diag"),
            new ShellTestHostOptions
            {
                ApplicationName = applicationName ?? ShellTestHost.DefaultApplicationName,
                Time = _time,
                ConfigureServices = configureServices,
                MapHost = mapHost,
            },
            AbortToken
        );
    }

    private static HeadlessDashboardSetupBuilder _ChooseMode(HeadlessDashboardSetupBuilder dashboard, AuthMode mode)
    {
        return mode switch
        {
            AuthMode.Host => dashboard.WithHostAuthentication(),
            AuthMode.Basic => dashboard.WithBasicAuth("ops", "secret"),
            AuthMode.ApiKey => dashboard.WithApiKey("k1"),
            _ => dashboard.WithNoAuth(),
        };
    }

    private static string[] _SessionCookieNames(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ?
            [
                .. SetCookieHeaderValue
                    .ParseList([.. values])
                    .Select(static cookie => cookie.Name.ToString())
                    .Where(static name => name.StartsWith("__Host-headless-dashboard", StringComparison.Ordinal)),
            ]
            : [];
    }

    // RFC 6265 cookie names are RFC 7230 tokens.
    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+\-.^_`|~]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex _CookieTokenRegex { get; }
}

/// <summary>Settings for one <see cref="ShellTestHost"/>.</summary>
internal sealed class ShellTestHostOptions
{
    /// <summary>The host's application name, which names the shell's session cookie.</summary>
    public string ApplicationName { get; init; } = ShellTestHost.DefaultApplicationName;

    public required TimeProvider Time { get; init; }

    /// <summary>Shared by two hosts that must read each other's cookies, as instances sharing one key ring do.</summary>
    public IDataProtectionProvider? DataProtection { get; init; }

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public Action<WebApplication>? MapHost { get; init; }
}

/// <summary>
/// A real host serving the dashboard shell at its default base path, with a host bearer scheme that signs in
/// <c>Authorization: Bearer &lt;role&gt;</c> as a user named for, and in, that role.
/// </summary>
internal static class ShellTestHost
{
    public const string DefaultApplicationName = "shell-tests";
    public const string DefaultCookieName = "__Host-headless-dashboard-shell-tests";
    public const string SessionPath = "/headless/api/auth/session";
    public const string InfoPath = "/headless/api/auth/info";
    public const string OpsPolicy = "Ops";
    public const string DiagViewersPolicy = "DiagViewers";
    public const string DiagOperatorsPolicy = "DiagOperators";

    public static FakeTimeProvider NewTime() => new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));

    public static async Task<WebApplication> StartAsync(
        Action<HeadlessDashboardSetupBuilder> configure,
        ShellTestHostOptions options,
        CancellationToken cancellationToken
    )
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { ApplicationName = options.ApplicationName }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(options.Time);

        if (options.DataProtection is { } dataProtection)
        {
            builder.Services.AddSingleton(dataProtection);
        }
        else
        {
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        }

        builder
            .Services.AddAuthentication(RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy(OpsPolicy, policy => policy.RequireRole("ops"))
            .AddPolicy(DiagViewersPolicy, policy => policy.RequireRole("ops", "diag-viewer", "diag-operator"))
            .AddPolicy(DiagOperatorsPolicy, policy => policy.RequireRole("ops", "diag-operator"));
        options.ConfigureServices?.Invoke(builder.Services);
        builder.Services.AddHeadlessDashboard(configure);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHeadlessDashboard();
        options.MapHost?.Invoke(app);

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

    public static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        // The session cookie is Secure, so the cookie handler issues and reads it only on an https request.
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    public static Task<HttpResponseMessage> ExchangeAsync(
        HttpClient client,
        string authorization,
        CancellationToken cancellationToken
    )
    {
        return SendAsync(client, HttpMethod.Post, SessionPath, cancellationToken, authorization: authorization);
    }

    /// <summary>Exchanges <paramref name="authorization"/> for a session and returns its cookie as a request header value.</summary>
    public static async Task<string> SignInAsync(
        HttpClient client,
        string authorization,
        CancellationToken cancellationToken,
        string cookieName = DefaultCookieName
    )
    {
        using var response = await ExchangeAsync(client, authorization, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var setCookie = SetCookie(response, cookieName);
        setCookie.Should().NotBeNull();

        return Cookie(setCookie!);
    }

    public static Task<HttpResponseMessage> GetDiagAsync(
        HttpClient client,
        string? cookie,
        CancellationToken cancellationToken,
        string? authorization = null
    )
    {
        return SendAsync(
            client,
            HttpMethod.Get,
            "/headless/api/diag/whoami",
            cancellationToken,
            authorization: authorization,
            cookie: cookie
        );
    }

    public static async Task<JsonElement> InfoAsync(
        HttpClient client,
        string? cookie,
        CancellationToken cancellationToken
    )
    {
        using var response = await SendAsync(client, HttpMethod.Get, InfoPath, cancellationToken, cookie: cookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }

    public static SetCookieHeaderValue? SetCookie(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue
                .ParseList([.. values])
                .FirstOrDefault(cookie => string.Equals(cookie.Name.Value, name, StringComparison.Ordinal))
            : null;
    }

    public static string Cookie(SetCookieHeaderValue setCookie) => $"{setCookie.Name}={setCookie.Value}";

    public static bool IsDeletion(SetCookieHeaderValue? setCookie)
    {
        return setCookie is { Expires: { } expires } && expires < DateTimeOffset.UnixEpoch.AddYears(1);
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        string? authorization = null,
        string? cookie = null,
        bool requestedWith = true
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (requestedWith)
        {
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Signs in <c>Authorization: Bearer &lt;role&gt;</c> as a user named for, and in, that role.</summary>
    private sealed class RoleBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HostBearer";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var role = header["Bearer ".Length..];
            var identity = new ClaimsIdentity([new(ClaimTypes.Name, role), new(ClaimTypes.Role, role)], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new(identity), SchemeName)));
        }
    }
}

/// <summary>
/// A module whose UI build is one of the fixture folders embedded in this assembly, with one read endpoint that answers
/// the signed-in user's name and one operator action.
/// </summary>
internal abstract class EndpointModule(string id) : IDashboardModule
{
    public string Id { get; } = id;

    public string DisplayName { get; } = id.ToUpperInvariant();

    public DashboardModuleAssets Assets { get; } =
        new() { Files = new EmbeddedFileProvider(typeof(EndpointModule).Assembly, $"Tests.Modules.Fixtures.{id}") };

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        endpoints.Routes.MapGet("/whoami", static (HttpContext context) => _NameOf(context));
        endpoints.RequireOperate(endpoints.Routes.MapPost("/requeue", () => "requeued"));
    }

    private static string _NameOf(HttpContext context)
    {
        return context.User.Identity is { IsAuthenticated: true, Name: { } name } ? name : "<anonymous>";
    }
}

internal sealed class DiagEndpointModule() : EndpointModule("diag");

internal sealed class AuditEndpointModule() : EndpointModule("audit");
