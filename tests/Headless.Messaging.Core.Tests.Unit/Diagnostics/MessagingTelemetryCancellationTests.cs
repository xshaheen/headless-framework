// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.Core;

namespace Tests.Diagnostics;

/// <summary>
/// Regression coverage for the caller-cancellation / guarded-send span fixes (commit e4dda16f9):
/// <see cref="DirectPublisherCore.SendAsync"/> and <see cref="MessageSender"/>'s transport-send guard must
/// stop the <c>message.publish</c> span un-errored on an <see cref="OperationCanceledException"/> rethrow,
/// instead of leaking it into <see cref="Activity.Current"/>.
/// </summary>
public sealed class MessagingTelemetryCancellationTests : TestBase
{
    private static readonly BrokerAddress _Broker = new("Test", "localhost");

    // (a) DirectPublisherCore: the shared send-and-trace kernel behind Bus/Queue. No DI required — it takes
    // every collaborator (serializer, transport delegate, telemetry) as a plain parameter.
    [Fact]
    public async Task should_stop_publish_span_without_error_status_when_direct_publisher_send_transport_cancels()
    {
        // given
        var message = _CreateMessage("orders.placed");
        var transportMessage = _CreateTransportMessage("orders.placed");
        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        Activity? published = null;
        using var stopped = new PublishActivityCollector();

        Task<OperateResult> SendTransport(TransportMessage sent, CancellationToken ct)
        {
            // message.publish has just been started by _TracingBeforeSend, so it is Activity.Current here.
            published = Activity.Current;
            throw new OperationCanceledException();
        }

        // when
        var act = () =>
            DirectPublisherCore.SendAsync(
                message,
                IntentType.Bus,
                serializer,
                _Broker,
                SendTransport,
                nowMs: () => 100,
                MessagingTelemetry.Default,
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();

        published.Should().NotBeNull();
        published!.OperationName.Should().Be("message.publish");
        stopped.All.Should().Contain(published);
        published.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    // (b) IMessageSender: MessageSender._SendWithoutRetryAsync's transport-send guard. transport.SendAsync
    // throwing (rather than returning a failed OperateResult) is not classified through the normal
    // MessagingRetryAttempt path, so Polly's ShouldHandle sees a bare exception outcome and rethrows —
    // the exception propagates all the way out of the public SendAsync call, matching the "propagates
    // unchanged" contract in the fix's own comment.
    [Fact]
    public async Task should_stop_publish_span_without_error_status_when_message_sender_transport_cancels()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .LeasePublishAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage("test.messageName");
        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        Activity? published = null;
        var busTransport = Substitute.For<IBusTransport>();
        busTransport.BrokerAddress.Returns(_Broker);
        busTransport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(
                (Func<CallInfo, OperateResult>)(
                    _ =>
                    {
                        published = Activity.Current;
                        throw new OperationCanceledException();
                    }
                )
            );

        using var stopped = new PublishActivityCollector();
        var sender = _CreateSender(storage, serializer, busTransport, new MessagingOptions());

        // when
        var act = () => sender.SendAsync(_CreateMediumMessage());

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();

        published.Should().NotBeNull();
        published!.OperationName.Should().Be("message.publish");
        stopped.All.Should().Contain(published);
        published.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    // (b, companion) The sibling guard: a non-cancellation throw from transport.SendAsync must record
    // messaging.publish.errors and mark the span Error (unlike the cancellation branch above) before
    // rethrowing — pinning that the two new catches in the fix are not accidentally symmetric.
    [Fact]
    public async Task should_record_publish_error_and_mark_span_error_when_message_sender_transport_throws()
    {
        // given
        var storage = Substitute.For<IDataStorage>();
        storage
            .LeasePublishAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        var transportMessage = _CreateTransportMessage("test.messageName");
        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(transportMessage));

        Activity? published = null;
        var busTransport = Substitute.For<IBusTransport>();
        busTransport.BrokerAddress.Returns(_Broker);
        busTransport
            .SendAsync(transportMessage, Arg.Any<CancellationToken>())
            .Returns(
                (Func<CallInfo, OperateResult>)(
                    _ =>
                    {
                        published = Activity.Current;
                        throw new InvalidOperationException("transport blew up");
                    }
                )
            );

        using var stopped = new PublishActivityCollector();
        var metrics = new List<(string Name, long Value)>();
        using var meters = _StartMeterListener(metrics);
        var sender = _CreateSender(storage, serializer, busTransport, new MessagingOptions());

        // when
        var act = () => sender.SendAsync(_CreateMediumMessage());

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();

        published.Should().NotBeNull();
        stopped.All.Should().Contain(published);
        published!.Status.Should().Be(ActivityStatusCode.Error);
        metrics.Should().Contain(m => string.Equals(m.Name, "messaging.publish.errors", StringComparison.Ordinal));
    }

    // --- Helpers --------------------------------------------------------------------------------------------

    private static MessageSender _CreateSender(
        IDataStorage storage,
        ISerializer serializer,
        IBusTransport busTransport,
        MessagingOptions options
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(storage);
        services.AddSingleton(serializer);
        services.AddSingleton(busTransport);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(options));

        var provider = services.BuildServiceProvider();
        return new MessageSender(provider.GetRequiredService<ILogger<MessageSender>>(), provider);
    }

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.messageName",
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

    private static Message _CreateMessage(string name)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
        };

        return new Message(headers, value: null);
    }

    private static TransportMessage _CreateTransportMessage(string name)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
        };

        return new TransportMessage(headers, new byte[] { 1, 2, 3 });
    }

    private static MeterListener _StartMeterListener(List<(string Name, long Value)> captured)
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

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) => captured.Add((instrument.Name, measurement))
        );

        listener.Start();

        return listener;
    }

    // Collects ActivityStopped events for the Headless.Messaging source.
    private sealed class PublishActivityCollector : IDisposable
    {
        private readonly ConcurrentBag<Activity> _stopped = [];
        private readonly ActivityListener _listener;

        public PublishActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source =>
                    string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Add,
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyCollection<Activity> All => _stopped;

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
