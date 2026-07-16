// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Diagnostics;

/// <summary>
/// T1 (pragmatic slice): drives ONE message through the real <see cref="ISubscribeExecutor"/> invoke path
/// — the subscriber-invoke half of the consume pipeline — and asserts the <c>subscriber.invoke</c> span
/// plus its <c>messaging.subscriber.invocations</c> instrument are produced by the actual call site
/// (<see cref="SubscribeExecutor.ExecuteAsync"/>), not by calling <see cref="MessagingTelemetry"/> directly.
/// The full transport-consume path (message.consume) is not exercised here — the harness for that
/// (real broker delivery through a runtime consumer) is materially heavier; see report for the tradeoff.
/// </summary>
public sealed class ConsumeTelemetryPipelineTests : TestBase
{
    private static readonly IServiceProvider _EmptyScope = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task should_emit_subscriber_invoke_span_and_metric_when_executor_runs_real_consumer()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.ForMessage<PipelineTestMessage>(message =>
                message.MessageName("test.pipeline.messageName").OnBus<PipelineTestConsumer>()
            );
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SubscribeExecutor>>();
        var options = Options.Create(
            new MessagingOptions
            {
                RetryPolicy =
                {
                    RetryStrategy = TestRetryStrategies.FixedDelay(0, TimeSpan.Zero),
                    MaxPersistedRetries = 0,
                },
            }
        );
        var circuitBreaker = Substitute.For<ICircuitBreakerStateManager>();
        var invoker = Substitute.For<ISubscribeInvoker>();
        invoker
            .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
            .Returns(
                new ConsumerExecutedResult(
                    result: null,
                    resultType: null,
                    msgId: "m-1",
                    callbackName: null,
                    callbackHeader: null
                )
            );

        var executor = new SubscribeExecutor(
            provider,
            storage,
            invoker,
            TimeProvider.System,
            logger,
            options,
            circuitBreaker
        );
        var message = _CreateMediumMessage();
        var descriptor = _CreateDescriptor();

        var spans = new List<Activity>();
        using var activityListener = _StartActivityListener(spans);
        var measurements = new List<string>();
        using var meterListener = _StartMeterListener(measurements);

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor, AbortToken);

        // then — produced by the real ExecuteAsync -> _InvokeConsumerMethodAsync call site.
        result.Succeeded.Should().BeTrue();
        spans.Should().Contain(a => string.Equals(a.OperationName, "subscriber.invoke", StringComparison.Ordinal));
        measurements.Should().Contain("messaging.subscriber.invocations");
    }

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.pipeline.messageName",
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            IntentType = IntentType.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var consumeMethod = typeof(IConsume<PipelineTestMessage>).GetMethod(
            nameof(IConsume<>.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<PipelineTestMessage>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            IntentType = IntentType.Bus,
            ServiceTypeInfo = typeof(PipelineTestConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(PipelineTestConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            MessageName = "test.pipeline.messageName",
            GroupName = "test",
            Parameters = consumeMethod
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name!,
                    ParameterType = p.ParameterType,
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
        };
    }

    private static ActivityListener _StartActivityListener(List<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static MeterListener _StartMeterListener(List<string> captured)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => captured.Add(instrument.Name));
        listener.Start();

        return listener;
    }
}

public sealed record PipelineTestMessage(string Id);

public sealed class PipelineTestConsumer : IConsume<PipelineTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<PipelineTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
