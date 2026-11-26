// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;
using Headless.Logging;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Tests.Simulation;

namespace Tests.Diagnostics;

/// <summary>
/// A consumer's log entries carry the trace id of the request that published the message, on the in-memory transport
/// (both lanes) and through the simulated broker, so an operator can follow the request into the consumer's lines.
/// </summary>
public sealed class ConsumeLogEnrichmentTests : TestBase
{
    private const string _CausationTraceId = "CausationTraceId";
    private const string _ConsumerLogMessage = "consumed the probe message";

    public override ValueTask InitializeAsync()
    {
        // The app's OpenTelemetry setup assigns the W3C propagator that writes traceparent at publish; no SDK provider
        // is built here, so assign the same one the other telemetry tests do.
        Sdk.SetDefaultTextMapPropagator(
            new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()])
        );
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_log_the_publishing_request_trace_id_on_the_consumer_entries_of_the_in_memory_transport(
        MessageLane lane
    )
    {
        // given: a host with the Headless logging profile
        using var listener = _ListenToMessaging();
        using var logs = new EnrichedLogCapture();
        using var host = await _StartInMemoryHostAsync(logs);
        var recorder = host.Services.GetRequiredService<ConsumeRecorder>();
        ActivityTraceId requestTraceId;

        // when: a request-like span publishes
        using (var request = _StartRequest("GET /orders"))
        {
            requestTraceId = request.TraceId;
            await using var scope = host.Services.CreateAsyncScope();

            if (lane == MessageLane.Bus)
            {
                await scope.ServiceProvider.GetRequiredService<IBus>().PublishAsync(new BusProbe("b1"), AbortToken);
            }
            else
            {
                await scope.ServiceProvider.GetRequiredService<IQueue>().EnqueueAsync(new QueueProbe("q1"), AbortToken);
            }
        }

        await recorder.Consumed.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        logs.Single(_ConsumerLogMessage).Fields.Should().Contain(_CausationTraceId, requestTraceId.ToHexString());
    }

    [Fact]
    public async Task should_carry_the_publishing_request_trace_id_into_the_consumer_of_a_broker_transport()
    {
        // given: the simulated broker carries the envelope like a real broker, through the real pipeline
        using var listener = _ListenToMessaging();
        var probe = new PipelineProbe();
        string? observed = null;
        probe.Behavior = (_, _, _) =>
        {
            observed = CausalScope.CausationTraceId;
            return ValueTask.CompletedTask;
        };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken);
        ActivityTraceId requestTraceId;

        // when
        using (var request = _StartRequest("POST /orders"))
        {
            requestTraceId = request.TraceId;
            await host.EnqueueAsync("o1", AbortToken);
        }

        await host.WithinBoundAsync(probe.WhenCompletedAsync("o1", 1), AbortToken);

        // then: the value the logging profile writes as CausationTraceId on every entry the consumer logs
        observed.Should().Be(requestTraceId.ToHexString());
        CausalScope.CausationTraceId.Should().BeNull("nothing leaks out of the consume");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-0000000000000000-00")]
    public void should_carry_no_trace_id_when_the_message_stored_no_readable_trace_context(string? traceParent)
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (traceParent is not null)
        {
            headers[Headers.TraceParent] = traceParent;
        }

        // when
        var traceId = ConsumeMiddlewarePipeline.PublisherTraceId(headers);

        // then
        traceId.Should().Be(default(ActivityTraceId));
    }

    [Fact]
    public void should_read_the_trace_id_of_a_well_formed_traceparent()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.TraceParent] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        };

        ConsumeMiddlewarePipeline
            .PublisherTraceId(headers)
            .ToHexString()
            .Should()
            .Be("4bf92f3577b34da6a3ce929d0e0e4736");
    }

    private static Activity _StartRequest(string name)
    {
        // Stands in for the ASP.NET Core request activity: a W3C span that is current while the request publishes.
#pragma warning disable CA2000 // False positive: the started activity is returned, and every caller disposes it.
        var request = new Activity(name);
#pragma warning restore CA2000
        request.SetIdFormat(ActivityIdFormat.W3C);
        return request.Start();
    }

    private static ActivityListener _ListenToMessaging()
    {
        // Messaging writes traceparent only while its source is observed, as it is under OpenTelemetry tracing.
        var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private async Task<IHost> _StartInMemoryHostAsync(EnrichedLogCapture logs)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddHeadlessLogging();
        builder.Logging.AddProvider(logs).AddProvider(LoggerProvider);
        builder.Services.AddSingleton<ConsumeRecorder>();
        builder.Services.ConfigureMessaging(messaging =>
        {
            messaging.Message<BusProbe>("tests.consume-log.bus-probe");
            messaging.Message<QueueProbe>("tests.consume-log.queue-probe");
        });
        builder.Services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<BusProbeConsumer>();
            messaging.AddConsumer<QueueProbeConsumer>();
            messaging.UseInMemory();
            messaging.UseProcessLocalInMemoryStorage();
        });

        var host = builder.Build();
        await host.Services.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return host;
    }

    private sealed record BusProbe(string Id);

    private sealed record QueueProbe(string Id);

    private sealed class ConsumeRecorder
    {
        private readonly TaskCompletionSource _consumed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Consumed => _consumed.Task;

        public void Record(ILogger logger)
        {
            logger.LogInformation(_ConsumerLogMessage);
            _consumed.TrySetResult();
        }
    }

    [BusConsumer("tests.consume-log.bus")]
    private sealed class BusProbeConsumer(ConsumeRecorder recorder, ILogger<BusProbeConsumer> logger)
        : IConsume<BusProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<BusProbe> context, CancellationToken cancellationToken)
        {
            recorder.Record(logger);
            return ValueTask.CompletedTask;
        }
    }

    [QueueConsumer("tests.consume-log.queue")]
    private sealed class QueueProbeConsumer(ConsumeRecorder recorder, ILogger<QueueProbeConsumer> logger)
        : IConsume<QueueProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<QueueProbe> context, CancellationToken cancellationToken)
        {
            recorder.Record(logger);
            return ValueTask.CompletedTask;
        }
    }
}
