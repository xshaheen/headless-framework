// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard;
using Headless.Emails;
using Headless.PushNotifications;
using Headless.Sms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// A real host serving the unified dashboard at <c>/headless</c> with the Dev inbox module over the real development
/// capture stores, each registered through its <c>UseDevelopment()</c> call. With <c>roles</c> it runs under host
/// authentication, where <c>Bearer ops</c> may view and operate and <c>Bearer viewer</c> may only view; otherwise it
/// runs with no authentication.
/// </summary>
internal static class DevInboxTestHost
{
    public const string Api = "/headless/api/dev-inbox";
    public const string Commands = Api + "/commands";

    /// <summary>The largest attachment the test hosts keep, so a larger one reads as omitted.</summary>
    public const int MaxCapturedAttachmentBytes = 1024;

    public static async Task<WebApplication> StartAsync(
        CancellationToken cancellationToken,
        string environment = "Development",
        bool emails = true,
        bool sms = true,
        bool push = true,
        bool roles = false,
        RecordingOperatorLog? log = null,
        int captureCapacity = 500,
        Action<HeadlessDashboardSetupBuilder>? dashboard = null,
        Action<IServiceCollection>? configure = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

        if (emails)
        {
            builder.Services.AddHeadlessEmails(setup =>
                setup.UseDevelopment(options =>
                {
                    options.MaxCapturedAttachmentBytes = MaxCapturedAttachmentBytes;
                    options.CaptureCapacity = captureCapacity;
                })
            );
        }

        if (sms)
        {
            builder.Services.AddHeadlessSms(setup =>
                setup.UseDevelopment(options => options.CaptureCapacity = captureCapacity)
            );
        }

        if (push)
        {
            builder.Services.AddHeadlessPushNotifications(setup =>
                setup.UseDevelopment(options => options.CaptureCapacity = captureCapacity)
            );
        }

        if (roles)
        {
            builder
                .Services.AddAuthentication(RoleBearerHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>(RoleBearerHandler.SchemeName, _ => { });
            builder
                .Services.AddAuthorizationBuilder()
                .AddPolicy("InboxViewers", policy => policy.RequireRole("ops", "viewer"))
                .AddPolicy("InboxOperators", policy => policy.RequireRole("ops"));
        }

        builder.Services.AddHeadlessDashboard(setup =>
        {
            if (roles)
            {
                setup.WithHostAuthentication(host =>
                {
                    host.ViewPolicy = "InboxViewers";
                    host.OperatePolicy = "InboxOperators";
                });
            }
            else
            {
                setup.WithNoAuth();
            }

            if (dashboard is null)
            {
                setup.AddDevInbox();
            }
            else
            {
                dashboard(setup);
            }
        });

        if (log is not null)
        {
            builder.Services.AddSingleton<IDashboardOperatorLog>(log);
        }

        configure?.Invoke(builder.Services);

        var app = builder.Build();

        if (roles)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

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
        // A session cookie is Secure, so it is issued and sent only on an https request.
        client.BaseAddress = new Uri("https://localhost");

        return client;
    }

    public static IEmailSender Emails(WebApplication app) => app.Services.GetRequiredService<IEmailSender>();

    public static ISmsSender Sms(WebApplication app) => app.Services.GetRequiredService<ISmsSender>();

    public static IPushNotificationService Push(WebApplication app) =>
        app.Services.GetRequiredService<IPushNotificationService>();

    public static IEmailCaptureStore EmailStore(WebApplication app) =>
        app.Services.GetRequiredService<IEmailCaptureStore>();

    public static ISmsCaptureStore SmsStore(WebApplication app) => app.Services.GetRequiredService<ISmsCaptureStore>();

    public static IPushNotificationCaptureStore PushStore(WebApplication app) =>
        app.Services.GetRequiredService<IPushNotificationCaptureStore>();

    /// <summary>
    /// Sends an email through the host's development sender and returns its capture. An email with no body is captured
    /// as failed, and the sender's refusal is not rethrown.
    /// </summary>
    public static async Task<CapturedEmail> SendEmailAsync(
        WebApplication app,
        string subject,
        CancellationToken cancellationToken,
        string? html = null,
        string? text = "Hello there.",
        IReadOnlyList<EmailRequestAttachment>? attachments = null
    )
    {
        try
        {
            await Emails(app)
                .SendAsync(
                    new SendSingleEmailRequest
                    {
                        From = new EmailRequestAddress("app@example.com", "The App"),
                        Destination = new EmailRequestDestination
                        {
                            ToAddresses = ["ada@example.com"],
                            CcAddresses = ["grace@example.com"],
                        },
                        Subject = subject,
                        MessageHtml = html,
                        MessageText = text,
                        Attachments = attachments ?? [],
                    },
                    cancellationToken
                );
        }
        catch (InvalidOperationException)
        {
            // The development sender captures an email with no body as failed, then refuses it.
        }

        return EmailStore(app).Query(new EmailCaptureQuery { Limit = 1 })[0];
    }

    /// <summary>Sends an SMS through the host's development sender and returns its capture.</summary>
    public static async Task<CapturedSms> SendSmsAsync(
        WebApplication app,
        string text,
        CancellationToken cancellationToken
    )
    {
        await Sms(app)
            .SendAsync(
                new SendSingleSmsRequest { Destination = new SmsRequestDestination(20, "1000000001"), Text = text },
                cancellationToken
            );

        return SmsStore(app).Query(new SmsCaptureQuery { Limit = 1 })[0];
    }

    /// <summary>Sends a push to one device through the host's development service and returns its capture.</summary>
    public static async Task<CapturedPushNotification> SendPushAsync(
        WebApplication app,
        PushNotificationRequest request,
        CancellationToken cancellationToken,
        string token = "device-token-1"
    )
    {
        await Push(app).SendToDeviceAsync(token, request, cancellationToken);

        return PushStore(app).Query(new PushNotificationCaptureQuery { Limit = 1 })[0];
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

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken,
        string? cookie = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonElement> GetJsonAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var response = await GetAsync(client, path, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await ReadAsync(response, cancellationToken);
    }

    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object body,
        CancellationToken cancellationToken,
        string? cookie = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };

        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("X-Headless-Dashboard-Channel", "ui");

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
