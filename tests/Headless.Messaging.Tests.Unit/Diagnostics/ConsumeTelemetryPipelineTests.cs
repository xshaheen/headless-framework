// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing;
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
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<PipelineTestMessage>("test.pipeline.messageName"));
        services.AddHeadlessMessaging(setup =>
        {
            setup.AddConsumer<PipelineTestConsumer>();
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
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

        var spans = new ConcurrentBag<Activity>();
        using var activityListener = _StartActivityListener(spans);
        using var meters = new TelemetryRecorder(MessagingDiagnostics.SourceName, spans: false);

        // when
        var result = await executor.ExecuteAsync(message, _EmptyScope, descriptor, AbortToken);

        // then — produced by the real ExecuteAsync -> _InvokeConsumerMethodAsync call site.
        result.Succeeded.Should().BeTrue();
        spans.Should().Contain(a => string.Equals(a.OperationName, "subscriber.invoke", StringComparison.Ordinal));
        meters.Measurements.Select(m => m.Instrument).Should().Contain("messaging.subscriber.invocations");
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
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        return new ConsumerExecutorDescriptor
        {
            Lane = MessageLane.Bus,
            ConsumerType = typeof(PipelineTestConsumer),
            MessageType = typeof(PipelineTestMessage),
            MessageName = "test.pipeline.messageName",
            SubscriptionName = "test",
        };
    }

    // Process-global callback: parallel tests' activities all land here — the collection must be thread-safe.
    private static ActivityListener _StartActivityListener(ConcurrentBag<Activity> captured)
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
}

public sealed record PipelineTestMessage(string Id);

[BusConsumer("tests.telemetry-pipeline")]
public sealed class PipelineTestConsumer : IConsume<PipelineTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<PipelineTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
