// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
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
        // given
        using var listener = _ListenToMessaging();
        using var logs = new EnrichedLogCapture();
        await using var provider = await _StartInMemoryHostAsync(logs);
        var recorder = provider.GetRequiredService<ConsumeRecorder>();
        ActivityTraceId requestTraceId;

        // when: a request-like span publishes
        using (var request = _StartRequest("GET /orders"))
        {
            requestTraceId = request.TraceId;
            await using var scope = provider.CreateAsyncScope();

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
    public async Task should_log_the_publishing_request_trace_id_on_the_consumer_entries_of_a_broker_transport()
    {
        // given: the simulated broker carries the envelope like a real broker, through the real pipeline
        using var listener = _ListenToMessaging();
        using var logs = new EnrichedLogCapture();
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            configureServices: services => services.AddLogging(logging => logging.EnableEnrichment().AddProvider(logs))
        );
        var consumerLogger = host.Services.GetRequiredService<ILogger<PipelineOrderConsumer>>();
        probe.Behavior = (_, _, _) =>
        {
            consumerLogger.LogInformation(_ConsumerLogMessage);
            return ValueTask.CompletedTask;
        };
        ActivityTraceId requestTraceId;

        // when
        using (var request = _StartRequest("POST /orders"))
        {
            requestTraceId = request.TraceId;
            await host.EnqueueAsync("o1", AbortToken);
        }

        await host.WithinBoundAsync(probe.WhenCompletedAsync("o1", 1), AbortToken);

        // then
        logs.Single(_ConsumerLogMessage).Fields.Should().Contain(_CausationTraceId, requestTraceId.ToHexString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-0000000000000000-00")]
    public void should_add_no_field_when_the_message_carries_no_readable_trace_context(string? traceParent)
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (traceParent is not null)
        {
            headers[Headers.TraceParent] = traceParent;
        }

        var collector = new TagCollector();

        // when
        using (ConsumeLogScope.Begin(headers))
        {
            new ConsumeLogEnricher().Enrich(collector);
        }

        // then
        collector.Tags.Should().BeEmpty();
    }

    [Fact]
    public void should_add_no_field_outside_a_consume_and_restore_the_outer_consume_after_a_nested_one()
    {
        // given
        const string outerTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var enricher = new ConsumeLogEnricher();
        var outside = new TagCollector();
        var afterNested = new TagCollector();

        // when
        enricher.Enrich(outside);
        using (ConsumeLogScope.Begin(new Dictionary<string, string?> { [Headers.TraceParent] = outerTraceParent }))
        {
            using (ConsumeLogScope.Begin(new Dictionary<string, string?>())) { }

            enricher.Enrich(afterNested);
        }

        // then
        outside.Tags.Should().BeEmpty();
        afterNested
            .Tags.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, object>(_CausationTraceId, "4bf92f3577b34da6a3ce929d0e0e4736"));
        ConsumeLogScope.CausationTraceId.Should().BeNull();
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

    private async Task<ServiceProvider> _StartInMemoryHostAsync(EnrichedLogCapture logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.EnableEnrichment().AddProvider(logs).AddProvider(LoggerProvider));
        services.AddSingleton<ConsumeRecorder>();
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<BusProbe>("tests.consume-log.bus-probe");
            messaging.Message<QueueProbe>("tests.consume-log.queue-probe");
        });
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<BusProbeConsumer>();
            messaging.AddConsumer<QueueProbeConsumer>();
            messaging.UseInMemory();
            messaging.UseProcessLocalInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
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

    private sealed class TagCollector : IEnrichmentTagCollector
    {
        public List<KeyValuePair<string, object>> Tags { get; } = [];

        public void Add(string tagName, object tagValue) => Tags.Add(new(tagName, tagValue));
    }
}
