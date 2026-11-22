// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Text;
using Headless.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace Tests;

/// <summary>One way code puts a sensitive value on a span or into propagated context.</summary>
internal sealed record SpanScenario(
    string Id,
    string Path,
    string Sentinel,
    Action<Activity, IDictionary<string, string>> Write
)
{
    public override string ToString() => Id;
}

internal static class SpanScenarios
{
    public static readonly IReadOnlyDictionary<string, SpanScenario> All = new SpanScenario[]
    {
        new(
            "s1",
            "span attribute: app tag with an e-mail",
            Pii.Email("s1"),
            (activity, _) => activity.SetTag("enduser.email", Pii.Email("s1"))
        ),
        new(
            "s2",
            "span attribute: app tag with a known shape (national ID)",
            Pii.NationalId(201),
            (activity, _) => activity.SetTag("app.national_id", Pii.NationalId(201))
        ),
        new(
            "s3",
            "span event: app event tag with an e-mail",
            Pii.Email("s3"),
            (activity, _) =>
                activity.AddEvent(
                    new ActivityEvent("signup", tags: new ActivityTagsCollection { ["user.email"] = Pii.Email("s3") })
                )
        ),
        new(
            "s4",
            "span event: Activity.AddException with a known shape in the message",
            Pii.NationalId(204),
            (activity, _) => activity.AddException(new InvalidOperationException("failed for " + Pii.NationalId(204)))
        ),
        new(
            "s5",
            "span status: description with a known shape",
            Pii.NationalId(205),
            (activity, _) => activity.SetStatus(ActivityStatusCode.Error, "failed for " + Pii.NationalId(205))
        ),
        new(
            "s6",
            "span attribute: exception.message tag with a known shape",
            Pii.NationalId(206),
            (activity, _) => activity.SetTag("exception.message", "failed for " + Pii.NationalId(206))
        ),
        new(
            "b1",
            "baggage: OpenTelemetry Baggage entry with an e-mail, injected for an outgoing call or message",
            Pii.Email("b1"),
            (activity, carrier) =>
            {
                var baggage = Baggage.Create().SetBaggage("user.email", Pii.Email("b1"));
                Propagators.DefaultTextMapPropagator.Inject(
                    new PropagationContext(activity.Context, baggage),
                    carrier,
                    static (headers, key, value) => headers[key] = value
                );
            }
        ),
        new(
            "b2",
            "baggage: Activity baggage with an e-mail, injected by the runtime propagator (HttpClient's)",
            Pii.Email("b2"),
            (activity, carrier) =>
            {
                activity.AddBaggage("user.email", Pii.Email("b2"));
                DistributedContextPropagator.Current.Inject(
                    activity,
                    carrier,
                    static (headers, key, value) =>
                    {
                        if (headers is IDictionary<string, string> dictionary && value is not null)
                        {
                            dictionary[key] = value;
                        }
                    }
                );
            }
        ),
    }.ToDictionary(s => s.Id, StringComparer.Ordinal);
}

/// <summary>A tracer provider exporting OTLP to a capture, with or without the Headless span processors.</summary>
internal static class SpanLeakHost
{
    public static Exported Run(SpanScenario scenario, Redaction redaction)
    {
        // A source of its own: every tracer provider in the process hears a shared source name, so a run in parallel
        // would otherwise export another run's raw span to this capture.
        var sourceName = "Leak.Spans." + Guid.NewGuid().ToString("N");
        var otlp = new OtlpCapture();
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production }
        );

        if (redaction == Redaction.On)
        {
            builder.Services.AddHeadlessRedaction(setup => setup.MaskText(Pii.NationalIdPattern));
        }

        builder
            .Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(sourceName);

                if (redaction == Redaction.On)
                {
                    // What AddHeadless() adds ahead of its exporter.
                    tracing.AddHeadlessLogPathRedaction().AddHeadlessTextRedaction();
                }

                tracing.AddOtlpExporter(exporter =>
                {
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Endpoint = otlp.Endpoint("traces");
                    exporter.HttpClientFactory = otlp.CreateClient;
                    exporter.ExportProcessorType = ExportProcessorType.Simple;
                });
            });

        using (var host = builder.Build())
        {
            // Building the provider registers its listener; the hosted service is not needed for that.
            _ = host.Services.GetRequiredService<TracerProvider>();
            using var source = new ActivitySource(sourceName);
            using var activity = source.StartActivity("operation-" + scenario.Id);

            activity.Should().NotBeNull("the tracer provider listens to the source");
            activity!.SetTag("leak.marker", Pii.Marker(scenario.Id));
            scenario.Write(activity, carrier);
        }

        var propagation = string.Join('\n', carrier.Select(pair => pair.Key + ": " + pair.Value));
        var exported = new Exported(
            new Dictionary<Sink, byte[]>
            {
                [Sink.OtlpSpans] = otlp.Bytes("traces"),
                [Sink.Propagation] = Encoding.UTF8.GetBytes(propagation),
            }
        );
        otlp.Dispose();

        return exported;
    }
}
