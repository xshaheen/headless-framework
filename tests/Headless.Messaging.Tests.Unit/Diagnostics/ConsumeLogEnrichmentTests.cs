// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;
using Headless.Logging;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
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
    private const string _TenantId = "TenantId";

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
        var probeId = lane == MessageLane.Bus ? "b1" : "q1";
        logs.Single($"{_ConsumerLogMessage} {probeId}")
            .Fields.Should()
            .Contain(_CausationTraceId, requestTraceId.ToHexString());
    }

    [Fact]
    public async Task should_keep_each_concurrent_consume_on_its_own_publisher_trace_id()
    {
        // given: two requests, each in its own trace, publish one message each
        using var listener = _ListenToMessaging();
        using var logs = new EnrichedLogCapture();
        using var host = await _StartInMemoryHostAsync(logs);
        var recorder = host.Services.GetRequiredService<ConsumeRecorder>();
        recorder.Expected = 2;
        ActivityTraceId busTraceId;
        ActivityTraceId queueTraceId;

        // when: each consume is held open until both have started, so the two run at the same time
        await using (var scope = host.Services.CreateAsyncScope())
        {
            using (var request = _StartRequest("POST /a"))
            {
                busTraceId = request.TraceId;
                await scope.ServiceProvider.GetRequiredService<IBus>().PublishAsync(new BusProbe("b2"), AbortToken);
            }

            using (var request = _StartRequest("POST /b"))
            {
                queueTraceId = request.TraceId;
                await scope.ServiceProvider.GetRequiredService<IQueue>().EnqueueAsync(new QueueProbe("q2"), AbortToken);
            }
        }

        await recorder.Consumed.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then: each consumer's entry carries only its own publisher's trace id
        busTraceId.Should().NotBe(queueTraceId);
        logs.Single($"{_ConsumerLogMessage} b2").Fields.Should().Contain(_CausationTraceId, busTraceId.ToHexString());
        logs.Single($"{_ConsumerLogMessage} q2").Fields.Should().Contain(_CausationTraceId, queueTraceId.ToHexString());
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_log_the_envelope_tenant_once_on_the_consumer_entries_of_a_console_only_host(
        MessageLane lane
    )
    {
        // given: the Headless logging profile with no OpenTelemetry, and tenant propagation on
        using var logs = new EnrichedLogCapture();
        using var host = await _StartInMemoryHostAsync(logs, propagateTenant: true);
        var recorder = host.Services.GetRequiredService<ConsumeRecorder>();

        // when: the message is published under a tenant
        using (host.Services.GetRequiredService<ICurrentTenant>().Change("acme"))
        {
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

        // then: the consumer's own entry gets the tenant from the enrichment
        var consumed = logs.Single(_ConsumerLogMessage);
        consumed.Keys.Should().ContainSingle(k => k == _TenantId);
        consumed.Fields[_TenantId].Should().Be("acme");

        // and the framework's tenant-switch entry, whose template already has {TenantId}, carries the key once
        var restored = logs
            .Entries.Should()
            .ContainSingle(x =>
                x.Message.StartsWith("Restoring ICurrentTenant from envelope", StringComparison.Ordinal)
            )
            .Subject;
        restored.Keys.Should().ContainSingle(k => k == _TenantId);
        restored.Fields[_TenantId].Should().Be("acme");
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
    [InlineData("ff-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    [InlineData("00-4BF92F3577B34DA6A3CE929D0E0E4736-00F067AA0BA902B7-01")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-extra")]
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

    private async Task<IHost> _StartInMemoryHostAsync(EnrichedLogCapture logs, bool propagateTenant = false)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddHeadlessLogging();
        builder.Logging.AddProvider(logs).AddProvider(LoggerProvider).SetMinimumLevel(LogLevel.Debug);

        if (propagateTenant)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        }

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
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allLogged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        private int _logged;

        /// <summary>How many consumes to hold open until all have started; set before publishing.</summary>
        public int Expected { get; set; } = 1;

        public Task Consumed => _allLogged.Task;

        public async ValueTask RecordAsync(ILogger logger, string probeId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _started) >= Expected)
            {
                _allStarted.TrySetResult();
            }

            // Waits for an event, never for time; the bound only fails a broken run instead of hanging it.
            await _allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            logger.LogInformation("consumed the probe message {ProbeId}", probeId);

            if (Interlocked.Increment(ref _logged) >= Expected)
            {
                _allLogged.TrySetResult();
            }
        }
    }

    [BusConsumer("tests.consume-log.bus")]
    private sealed class BusProbeConsumer(ConsumeRecorder recorder, ILogger<BusProbeConsumer> logger)
        : IConsume<BusProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<BusProbe> context, CancellationToken cancellationToken)
        {
            return recorder.RecordAsync(logger, context.Message.Id, cancellationToken);
        }
    }

    [QueueConsumer("tests.consume-log.queue")]
    private sealed class QueueProbeConsumer(ConsumeRecorder recorder, ILogger<QueueProbeConsumer> logger)
        : IConsume<QueueProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<QueueProbe> context, CancellationToken cancellationToken)
        {
            return recorder.RecordAsync(logger, context.Message.Id, cancellationToken);
        }
    }
}
