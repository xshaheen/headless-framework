// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Headless.Jobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> under host authentication with the Jobs module.
/// <c>Bearer ops</c> may view and operate, <c>Bearer viewer</c> may only view.
/// </summary>
internal static class JobsDashboardHttpHost
{
    public const string Commands = "/headless/api/jobs/commands";
    public const string Api = "/headless/api/jobs";

    public static async Task<WebApplication> StartAsync(
        FakeTimeProvider time,
        RecordingOperatorLog log,
        CancellationToken cancellationToken,
        bool addJobs = true
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("JobsViewers", policy => policy.RequireRole("ops", "viewer"))
            .AddPolicy("JobsOperators", policy => policy.RequireRole("ops"));

        if (addJobs)
        {
            builder.Services.AddHeadlessJobs(jobs => jobs.DisableBackgroundServices().AddModule<DashboardTestJobs>());
        }

        builder.Services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication(host =>
                {
                    host.ViewPolicy = "JobsViewers";
                    host.OperatePolicy = "JobsOperators";
                })
                .AddJobs()
        );
        builder.Services.AddSingleton<IDashboardOperatorLog>(log);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        try
        {
            app.MapHeadlessDashboard();
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
        // The session cookie is Secure, so it is issued and sent only on an https request.
        client.BaseAddress = new Uri("https://localhost");

        return client;
    }

    /// <summary>Exchanges <c>Bearer {role}</c> for a dashboard session and returns its cookie header value.</summary>
    public static async Task<string> SignInAsync(HttpClient client, string role, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/headless/api/auth/session", UriKind.Relative)
        );
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + role);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var cookie = response
            .Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("__Host-", StringComparison.Ordinal));

        return cookie[..cookie.IndexOf(';', StringComparison.Ordinal)];
    }

    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object body,
        string cookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        string cookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }

    /// <summary>Signs in <c>Authorization: Bearer &lt;role&gt;</c> as a user named for, and in, that role.</summary>
    private sealed class RoleBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestBearer";

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

/// <summary>Records every operator-log entry the shell writes.</summary>
internal sealed class RecordingOperatorLog : IDashboardOperatorLog
{
    private readonly Lock _lock = new();
    private readonly List<OperatorLogEntry> _entries = [];

    public IReadOnlyList<OperatorLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ValueTask WriteAsync(OperatorLogEntry entry, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }

        return ValueTask.CompletedTask;
    }
}
