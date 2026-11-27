// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.Leases;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> under host authentication with the Leases module.
/// <c>Bearer ops</c> may view and operate, <c>Bearer viewer</c> may only view.
/// </summary>
internal static class LeasesDashboardHost
{
    public const string Api = "/headless/api/leases";

    public static DateTimeOffset Now { get; } = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Starts the host and returns it once it serves requests.</summary>
    /// <param name="fencing">Registers in-memory Fencing before the dashboard.</param>
    /// <param name="locks">Registers the in-memory lock provider before the dashboard.</param>
    /// <param name="before">Registers services before the dashboard, such as a substitute inspector or lock.</param>
    /// <param name="after">Registers services after the dashboard.</param>
    /// <param name="addLeases">Adds the module; <c>AddLeases()</c> by default.</param>
    public static async Task<WebApplication> StartAsync(
        CancellationToken cancellationToken,
        FakeTimeProvider? time = null,
        bool fencing = false,
        bool locks = false,
        Action<IServiceCollection>? before = null,
        Action<IServiceCollection>? after = null,
        Func<HeadlessDashboardSetupBuilder, HeadlessDashboardSetupBuilder>? addLeases = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(time ?? new FakeTimeProvider(Now));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("LeaseViewers", policy => policy.RequireRole("ops", "viewer"))
            .AddPolicy("LeaseOperators", policy => policy.RequireRole("ops"));

        if (fencing)
        {
            builder.Services.AddHeadlessFencing(setup => setup.UseInMemory());
        }

        if (locks)
        {
            builder.Services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());
        }

        before?.Invoke(builder.Services);
        builder.Services.AddHeadlessDashboard(dashboard =>
        {
            dashboard.WithHostAuthentication(host =>
            {
                host.ViewPolicy = "LeaseViewers";
                host.OperatePolicy = "LeaseOperators";
            });

            if (addLeases is null)
            {
                dashboard.AddLeases();
            }
            else
            {
                addLeases(dashboard);
            }
        });
        after?.Invoke(builder.Services);

        var app = builder.Build();
        // The test server rethrows an unhandled exception into the client, where Kestrel answers 500; this answers as
        // Kestrel does, so a test sees the status a browser would.
        app.Use(
            static async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (InvalidOperationException) when (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            }
        );
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

    /// <summary>Grants a lease through the host's own Fencing, under <paramref name="tenantId"/> or the host scope.</summary>
    public static async Task<FencedLease> GrantAsync(
        WebApplication app,
        string? tenantId,
        string kind,
        string resource,
        CancellationToken cancellationToken,
        TimeSpan? duration = null,
        string? holder = null
    )
    {
        var leases = app.Services.GetRequiredService<IFencedLeases>();

        using (app.Services.GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            var result = await leases.GrantAsync(
                kind,
                resource,
                duration ?? TimeSpan.FromMinutes(5),
                new LeaseGrantOptions { Holder = holder },
                cancellationToken
            );

            result.IsAcquired.Should().BeTrue();

            return result.Lease!;
        }
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
        string? cookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Signs in as <paramref name="role"/>, reads <paramref name="path"/>, and returns the response.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        WebApplication app,
        string path,
        CancellationToken cancellationToken,
        string role = "ops"
    )
    {
        using var client = Client(app);
        var cookie = await SignInAsync(client, role, cancellationToken);

        return await GetAsync(client, path, cookie, cancellationToken);
    }

    /// <summary>Signs in as <paramref name="role"/>, reads <paramref name="path"/>, and checks it answered 200.</summary>
    public static async Task<JsonElement> GetJsonAsync(
        WebApplication app,
        string path,
        CancellationToken cancellationToken,
        string role = "ops"
    )
    {
        using var response = await SendAsync(app, path, cancellationToken, role);
        response
            .StatusCode.Should()
            .Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return await ReadAsync(response, cancellationToken);
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
