// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Headless.Api.ServiceDefaults;
using Headless.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Tests;

/// <summary>A request that carries a sensitive value into ASP.NET Core's own logs, HTTP logging, and the request span.</summary>
internal sealed record HostScenario(
    string Id,
    string Path,
    string Sentinel,
    bool RedactPath,
    Func<HttpClient, CancellationToken, Task> Send
)
{
    public override string ToString() => Id;
}

internal static class HostScenarios
{
    public static readonly IReadOnlyDictionary<string, HostScenario> All = new HostScenario[]
    {
        new(
            "h1",
            "request path segment with an e-mail",
            Pii.Email("h1"),
            RedactPath: false,
            async (client, token) =>
            {
                using var response = await client.GetAsync($"/marker-h1/users/{Pii.Email("h1")}/orders", token);
            }
        ),
        new(
            "h1b",
            "request path segment with an e-mail, an ILogPathRedactor declared for the route",
            Pii.Email("h1b"),
            RedactPath: true,
            async (client, token) =>
            {
                using var response = await client.GetAsync($"/marker-h1b/users/{Pii.Email("h1b")}/orders", token);
            }
        ),
        new(
            "h2",
            "query string value with an e-mail",
            Pii.Email("h2"),
            RedactPath: false,
            async (client, token) =>
            {
                using var response = await client.GetAsync($"/marker-h2/orders?email={Pii.Email("h2")}", token);
            }
        ),
        new(
            "h3",
            "Authorization: Bearer header",
            Pii.Token("h3"),
            RedactPath: false,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/marker-h3/orders");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Pii.Token("h3"));
                using var response = await client.SendAsync(request, token);
            }
        ),
        new(
            "h4",
            "custom request header with an e-mail (X-User-Email)",
            Pii.Email("h4"),
            RedactPath: false,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/marker-h4/orders");
                request.Headers.Add("X-User-Email", Pii.Email("h4"));
                using var response = await client.SendAsync(request, token);
            }
        ),
        new(
            "h5",
            "unhandled endpoint exception with a known shape in the message",
            Pii.NationalId(305),
            RedactPath: false,
            async (client, token) =>
            {
                try
                {
                    using var response = await client.GetAsync("/marker-h5/fail", token);
                }
                catch (InvalidOperationException)
                {
                    // The test server rethrows an exception that escaped the app; the span already recorded it.
                }
            }
        ),
    }.ToDictionary(s => s.Id, StringComparer.Ordinal);
}

/// <summary>
/// Hosts the endpoints on a test server. Off is a plain ASP.NET Core host with OpenTelemetry and HTTP logging as their
/// packages ship them. On is <c>AddHeadless()</c> with the documented redaction and <c>AddHeadlessHttpLoggingRedaction()</c>.
/// </summary>
internal static class HostLeakHost
{
    public static async Task<Exported> RunAsync(
        HostScenario scenario,
        Redaction redaction,
        CancellationToken cancellationToken
    )
    {
        var otlp = new OtlpCapture();
#pragma warning disable CA2000 // False positive: AddProcessor hands the processor to the tracer provider, which disposes it with the host.
        var serverSpanEnded = new ServerSpanEnded();
#pragma warning restore CA2000
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        void exportLogs(OpenTelemetryLoggerOptions logging) =>
            logging.AddOtlpExporter(
                (exporter, processor) =>
                {
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Endpoint = otlp.Endpoint("logs");
                    exporter.HttpClientFactory = otlp.CreateClient;
                    processor.ExportProcessorType = ExportProcessorType.Simple;
                }
            );

        void exportSpans(TracerProviderBuilder tracing) =>
            tracing
                .AddOtlpExporter(exporter =>
                {
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Endpoint = otlp.Endpoint("traces");
                    exporter.HttpClientFactory = otlp.CreateClient;
                    exporter.ExportProcessorType = ExportProcessorType.Simple;
                })
                // After the exporter, so the span is exported by the time the test reads the capture.
                .AddProcessor(serverSpanEnded);

        if (redaction == Redaction.On)
        {
            builder.AddHeadless(options =>
            {
                options.Validation.ValidateServiceProviderOnStartup = false;
                options.Validation.RequireUseHeadless = false;
                options.Validation.RequireMapHeadlessEndpoints = false;
                options.Validation.RequireStatusCodesRewriter = false;
                options.OpenApi.Enabled = false;
                options.OpenTelemetry.ConfigureLogging = exportLogs;
                options.OpenTelemetry.ConfigureTracing = exportSpans;
            });
            builder.Services.AddHeadlessRedaction(setup =>
                setup.MaskText(Pii.NationalIdPattern).ClassifyProperty("Email", HeadlessDataClassifications.Private)
            );
            builder.Services.AddHeadlessHttpLoggingRedaction();

            if (scenario.RedactPath)
            {
                builder.Services.AddSingleton<ILogPathRedactor, UserPathRedactor>();
            }
        }
        else
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                exportLogs(logging);
            });
            builder
                .Services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing.AddAspNetCoreInstrumentation(instrumentation => instrumentation.RecordException = true);
                    exportSpans(tracing);
                });
            builder.Services.AddHttpLogging(_ => { });
        }

        await using (var app = builder.Build())
        {
            app.UseHttpLogging();
            app.MapGet("/marker-{id}/users/{user}/orders", (string id, string user) => "ok");
            app.MapGet("/marker-{id}/orders", (string id) => "ok");
            app.MapGet(
                "/marker-{id}/fail",
                string (string id) => throw new InvalidOperationException("failed for " + Pii.NationalId(305))
            );

            await app.StartAsync(cancellationToken);
            using var client = app.GetTestClient();

            await scenario.Send(client, cancellationToken);
            await serverSpanEnded.Task.WaitAsync(cancellationToken);
            await app.StopAsync(cancellationToken);
        }

        var exported = new Exported(
            new Dictionary<Sink, byte[]>
            {
                [Sink.OtlpLogs] = otlp.Bytes("logs"),
                [Sink.OtlpSpans] = otlp.Bytes("traces"),
            }
        );
        otlp.Dispose();

        return exported;
    }

    /// <summary>Completes when the request span ends, the last thing the hosting layer does for a request.</summary>
    private sealed class ServerSpanEnded : BaseProcessor<Activity>
    {
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _ended.Task;

        public override void OnEnd(Activity data)
        {
            if (data.Kind == ActivityKind.Server)
            {
                _ended.TrySetResult();
            }
        }
    }

    /// <summary>Declares the user segment of <c>/marker-*/users/{user}/orders</c> as one to keep out of logs and spans.</summary>
    private sealed class UserPathRedactor : ILogPathRedactor
    {
        public bool TryRedact(string path, [NotNullWhen(true)] out string? redacted)
        {
            var start = path.IndexOf("/users/", StringComparison.Ordinal);
            var end = start < 0 ? -1 : path.IndexOf('/', start + "/users/".Length);

            if (start < 0 || end < 0)
            {
                redacted = null;
                return false;
            }

            redacted = path[..(start + "/users/".Length)] + "{user}" + path[end..];
            return true;
        }
    }
}
