// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Module;

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> under host authentication with the Messaging module
/// over the in-memory store. <c>Bearer ops</c> may view and operate, <c>Bearer viewer</c> may only view, and each signs
/// in as a user named for its role.
/// </summary>
internal static class MessagingCommandHost
{
    public const string Commands = "/headless/api/messaging/commands";

    public static async Task<WebApplication> StartAsync(
        TimeProvider time,
        RecordingOperatorLog log,
        CancellationToken cancellationToken,
        string nodeName = "node-a",
        IDataStorage? sharedStorage = null,
        IDataProtectionProvider? sharedProtection = null,
        Action<IServiceCollection>? configure = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder
            .Services.AddAuthentication(RoleBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
        builder
            .Services.AddAuthorizationBuilder()
            .AddPolicy("MessagingViewers", policy => policy.RequireRole("ops", "viewer"))
            .AddPolicy("MessagingOperators", policy => policy.RequireRole("ops"));
        builder.Services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        if (sharedStorage is not null)
        {
            builder.Services.AddSingleton(sharedStorage);
        }

        builder.Services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication(host =>
                {
                    host.ViewPolicy = "MessagingViewers";
                    host.OperatePolicy = "MessagingOperators";
                })
                .AddMessaging(options => options.NodeName = nodeName)
        );
        builder.Services.AddSingleton<IDashboardOperatorLog>(log);

        // Nodes of one deployment share their data-protection keys, so a scope token confirmed on one node runs on
        // another.
        if (sharedProtection is not null)
        {
            builder.Services.AddSingleton(sharedProtection);
        }

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

    /// <summary>
    /// Admits a received row as an inbox generation and fails it for good. The inbox commands act on a generation's
    /// incarnation, so a row stored without one is refused as not supported by the store.
    /// </summary>
    public static async Task<MediumMessage> InboxFailureAsync(
        IDataStorage storage,
        string messageId,
        CancellationToken cancellationToken
    )
    {
        var admitted = await storage.AdmitReceivedMessageAsync(
            MessagingRows.Name,
            MessagingRows.Consumer,
            "v1",
            MessagingRows.Medium(messageId),
            cancellationToken: cancellationToken
        );
        var message = admitted.Message;
        message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(1), 0, cancellationToken))
            .Should()
            .BeTrue();
        message.FailureFingerprint = "v1:card-declined";
        (
            await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Failed,
                MessageContentWrite.Preserve,
                retryDelay: null,
                lockedUntil: null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                cancellationToken
            )
        )
            .Should()
            .BeTrue();

        return message;
    }

    /// <summary>A circuit in <paramref name="state"/> after five failures in a row.</summary>
    public static CircuitBreakerSnapshot Snapshot(CircuitBreakerState state) =>
        new()
        {
            State = state,
            EscalationLevel = 0,
            OpenedAt = null,
            EstimatedRemainingOpenDuration = null,
            ConsecutiveFailures = 5,
            FailureThreshold = 5,
            EffectiveOpenDuration = TimeSpan.FromSeconds(30),
        };

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
