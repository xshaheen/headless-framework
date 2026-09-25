// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>A request the fake APNs server received.</summary>
/// <param name="Protocol">The HTTP protocol the request arrived on, such as <c>HTTP/2</c>.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The request path, such as <c>/3/device/&lt;token&gt;</c>.</param>
/// <param name="DeviceToken">The device token taken from the path.</param>
/// <param name="Headers">The request headers, keyed case-insensitively.</param>
/// <param name="Body">The request body as UTF-8 text.</param>
/// <param name="Bearer">The provider token from the <c>authorization: bearer</c> header, if any.</param>
/// <param name="Attempt">The 1-based count of requests the server has received for this device token.</param>
public sealed record FakeApnsRequest(
    string Protocol,
    string Method,
    string Path,
    string DeviceToken,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    string? Bearer,
    int Attempt
);

/// <summary>The answer the fake APNs server gives to one request.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Reason">The APNs <c>reason</c> written into a JSON error body; ignored for 200.</param>
/// <param name="RawBody">A literal response body that replaces the JSON error body when set.</param>
/// <param name="AbortAfterRead">
/// Resets the stream after the request body was read instead of answering, which is how a connection lost after APNs
/// accepted a notification looks to the client.
/// </param>
/// <param name="UniqueId">
/// The <c>apns-unique-id</c> response header, which the APNs sandbox adds to identify the notification in its delivery
/// log; omitted when <see langword="null"/>.
/// </param>
public sealed record FakeApnsReply(
    int Status,
    string? Reason = null,
    string? RawBody = null,
    bool AbortAfterRead = false,
    string? UniqueId = null
)
{
    public static FakeApnsReply Ok { get; } = new(200);

    public static FakeApnsReply Abort { get; } = new(200, AbortAfterRead: true);
}

/// <summary>
/// An in-process APNs double: Kestrel on a loopback port speaking cleartext HTTP/2 (h2c prior knowledge), so the
/// provider's real <see cref="SocketsHttpHandler"/> HTTP/2 path runs end to end.
/// </summary>
public sealed class FakeApnsServer : IAsyncDisposable
{
    public const string KeyId = "ABC123DEFG";
    public const string TeamId = "TEAM123456";
    public const string BundleId = "com.example.app";

    private readonly ConcurrentQueue<FakeApnsRequest> _requests = new();
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly WebApplication _app;
    private int _inFlight;
    private long _maxInFlight;

    private FakeApnsServer(WebApplication app)
    {
        _app = app;
        PrivateKeyPem = _key.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>The loopback h2c address the server listens on.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>The PEM text of a P-256 key whose public half verifies the provider tokens the server receives.</summary>
    public string PrivateKeyPem { get; }

    /// <summary>Decides the answer to each request. Defaults to accepting everything.</summary>
    public Func<FakeApnsRequest, FakeApnsReply> Responder { get; set; } = static _ => FakeApnsReply.Ok;

    /// <summary>A delay applied before answering, so concurrent requests overlap and in-flight counts mean something.</summary>
    public TimeSpan ResponseDelay { get; set; }

    public IReadOnlyList<FakeApnsRequest> Requests => [.. _requests];

    public long MaxInFlight => Volatile.Read(ref _maxInFlight);

    public static async Task<FakeApnsServer> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2);
            // The provider may fan out up to 1000 concurrent streams; keep the fake from being the bottleneck.
            kestrel.Limits.Http2.MaxStreamsPerConnection = 1000;
        });

        var app = builder.Build();
        var server = new FakeApnsServer(app);
        app.Run(server._HandleAsync);

        await app.StartAsync(cancellationToken);
        server.BaseAddress = new Uri(app.Urls.First());

        return server;
    }

    /// <summary>Returns the bearer values the server has seen, without duplicates, in arrival order.</summary>
    public IReadOnlyList<string> DistinctBearers()
    {
        return [.. _requests.Select(r => r.Bearer).OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Verifies a provider token's ES256 signature against the public half of <see cref="PrivateKeyPem"/>.</summary>
    public bool VerifyJwt(string jwt)
    {
        var segments = jwt.Split('.');

        if (segments.Length != 3)
        {
            return false;
        }

        return _key.VerifyData(
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
            Base64Url.DecodeFromChars(segments[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
    }

    /// <summary>Options that point at this server's key identity, with any overrides applied.</summary>
    public void ConfigureOptions(ApnsOptions options)
    {
        options.KeyId = KeyId;
        options.TeamId = TeamId;
        options.PrivateKey = PrivateKeyPem;
        options.BundleId = BundleId;
    }

    /// <summary>
    /// Builds a container whose default push service is APNs aimed at this server, with zero retry delay so retry
    /// scenarios run instantly.
    /// </summary>
    public ServiceProvider CreateProvider(
        Action<ApnsOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        Action<HttpStandardResilienceOptions>? configureResilience = null,
        ILoggerProvider? loggerProvider = null,
        Action<HttpClient>? configureClient = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);

            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });

        configureServices?.Invoke(services);

        services.AddHeadlessPushNotifications(setup =>
            setup.UseApns(
                options =>
                {
                    ConfigureOptions(options);
                    configure?.Invoke(options);
                },
                configureClient: client =>
                {
                    client.BaseAddress = BaseAddress;
                    configureClient?.Invoke(client);
                },
                configureResilience: resilience =>
                {
                    resilience.Retry.Delay = TimeSpan.Zero;
                    configureResilience?.Invoke(resilience);
                }
            )
        );

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _key.Dispose();
    }

    private async Task _HandleAsync(HttpContext context)
    {
        var inFlight = Interlocked.Increment(ref _inFlight);
        _maxInFlight.InterlockedRaiseTo(inFlight);

        try
        {
            var request = context.Request;
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(context.RequestAborted);

            const string devicePrefix = "/3/device/";
            var path = request.Path.Value ?? "";
            var deviceToken = path.StartsWith(devicePrefix, StringComparison.Ordinal)
                ? Uri.UnescapeDataString(path[devicePrefix.Length..])
                : "";

            var headers = request.Headers.ToDictionary(
                h => h.Key,
                h => h.Value.ToString(),
                StringComparer.OrdinalIgnoreCase
            );

            var authorization = request.Headers.Authorization.ToString();
            var bearer = authorization.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorization["bearer ".Length..]
                : null;

            var attempt = _attempts.AddOrUpdate(deviceToken, 1, static (_, count) => count + 1);

            var recorded = new FakeApnsRequest(
                request.Protocol,
                request.Method,
                path,
                deviceToken,
                headers,
                body,
                bearer,
                attempt
            );
            _requests.Enqueue(recorded);

            if (ResponseDelay > TimeSpan.Zero)
            {
                await Task.Delay(ResponseDelay, context.RequestAborted);
            }

            var reply = Responder(recorded);

            if (reply.AbortAfterRead)
            {
                context.Abort();

                return;
            }

            context.Response.StatusCode = reply.Status;

            if (headers.TryGetValue("apns-id", out var apnsId))
            {
                context.Response.Headers["apns-id"] = apnsId;
            }

            if (reply.UniqueId is not null)
            {
                context.Response.Headers["apns-unique-id"] = reply.UniqueId;
            }

            if (reply.RawBody is not null)
            {
                await context.Response.WriteAsync(reply.RawBody, context.RequestAborted);
            }
            else if (reply.Status != 200)
            {
                context.Response.ContentType = "application/json";
                var errorBody =
                    reply.Status == 410
                        ? JsonSerializer.Serialize(new { reason = reply.Reason, timestamp = 1_758_000_000_000L })
                        : JsonSerializer.Serialize(new { reason = reply.Reason });
                await context.Response.WriteAsync(errorBody, context.RequestAborted);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
