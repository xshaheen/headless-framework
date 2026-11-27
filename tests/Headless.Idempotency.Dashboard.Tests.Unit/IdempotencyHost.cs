// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Caching;
using Headless.Dashboard;
using Headless.Idempotency;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>The idempotency store a test host runs on.</summary>
public enum IdempotencyTestStore
{
    InMemory = 0,
    Cache = 1,
}

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> under host authentication with the Idempotency module
/// over a real idempotency provider. <c>Bearer ops</c> may view and operate, <c>Bearer viewer</c> may only view.
/// </summary>
internal static class IdempotencyHost
{
    public const string Api = "/headless/api/idempotency";

    public static async Task<WebApplication> StartAsync(
        TimeProvider time,
        CancellationToken cancellationToken,
        IdempotencyTestStore store = IdempotencyTestStore.InMemory,
        bool addIdempotency = true,
        Action<HeadlessDashboardSetupBuilder>? addModule = null,
        Action<IdempotentOperationsOptions>? options = null,
        Action<IServiceCollection>? configure = null,
        ILoggerProvider? logs = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.AddSingleton(time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("Viewers", policy => policy.RequireRole("ops", "viewer"))
            .AddPolicy("Operators", policy => policy.RequireRole("ops"));

        if (addIdempotency)
        {
            if (store == IdempotencyTestStore.Cache)
            {
                builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
            }

            builder.Services.AddHeadlessIdempotency(setup =>
            {
                if (store == IdempotencyTestStore.Cache)
                {
                    setup.UseCache();
                }
                else
                {
                    setup.UseInMemory();
                }

                if (options is not null)
                {
                    setup.ConfigureOptions(options);
                }
            });
        }

        builder.Services.AddHeadlessDashboard(dashboard =>
        {
            dashboard.WithHostAuthentication(host =>
            {
                host.ViewPolicy = "Viewers";
                host.OperatePolicy = "Operators";
            });

            if (addModule is null)
            {
                dashboard.AddIdempotency();
            }
            else
            {
                addModule(dashboard);
            }
        });
        configure?.Invoke(builder.Services);

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

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content,
        string? cookie,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");

        return await client.SendAsync(request, cancellationToken);
    }

    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object body,
        string? cookie,
        CancellationToken cancellationToken
    ) => SendAsync(client, HttpMethod.Post, path, JsonContent.Create(body), cookie, cancellationToken);

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        string? cookie,
        CancellationToken cancellationToken
    ) => SendAsync(client, HttpMethod.Get, path, content: null, cookie, cancellationToken);

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.Clone();
    }

    /// <summary>Signs in <c>Authorization: Bearer &lt;role&gt;</c> as a user named for, and in, that role.</summary>
    internal sealed class RoleBearerHandler(
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

/// <summary>Records every log entry with its rendered message and attached exception.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly Lock _lock = new();
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Recorder(this);

    public void Dispose() { }

    private sealed class Recorder(RecordingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (owner._lock)
            {
                owner._entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
