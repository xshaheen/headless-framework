// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Headless.Messaging;
using Headless.Messaging.Transport;

namespace Tests.Transport;

/// <summary>
/// A transport outside this repository records what only it can see on the core's instruments, with the same tags the
/// built-in transports use.
/// </summary>
public sealed class TransportMetricsTests
{
    [Fact]
    public void should_record_a_settlement_failure_tagged_with_system_operation_and_error_type()
    {
        // given — a system name no other test uses, because the meter is process-wide
        var system = "test-" + Guid.NewGuid().ToString("N");
        using var recorder = new TagRecorder(
            MessagingMetrics.SettlementFailuresName,
            MessagingMetrics.TagSystem,
            system
        );

        // when
        TransportMetrics.RecordSettlementFailure(
            system,
            TransportMetrics.OperationAck,
            TransportMetrics.ErrorTypeChannelClosed
        );

        // then
        var tags = recorder.Single();
        tags[MessagingMetrics.TagOperationName].Should().Be("ack");
        tags[MessagingMetrics.TagErrorType].Should().Be("channel_closed");
    }

    [Fact]
    public void should_record_an_every_instance_drop_as_the_dropped_outcome()
    {
        // given
        var identity = "test-" + Guid.NewGuid().ToString("N");
        using var recorder = new TagRecorder(
            MessagingMetrics.EveryInstanceDeliveriesName,
            MessagingMetrics.TagConsumerGroupName,
            identity
        );

        // when
        TransportMetrics.RecordEveryInstanceDrop(identity, "overflow");

        // then
        var tags = recorder.Single();
        tags[MessagingMetrics.TagEveryInstanceOutcome].Should().Be("dropped");
        tags[MessagingMetrics.TagErrorType].Should().Be("overflow");
    }

    [Fact]
    public void should_name_the_error_type_by_the_full_exception_type_name()
    {
        TransportMetrics.ErrorTypeOf(new TimeoutException()).Should().Be("System.TimeoutException");
    }

    [Fact]
    public void should_reject_blank_arguments()
    {
        FluentActions
            .Invoking(() => TransportMetrics.RecordSettlementFailure(" ", "ack", "x"))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() => TransportMetrics.RecordEveryInstanceDrop("identity", ""))
            .Should()
            .Throw<ArgumentException>();
        FluentActions.Invoking(() => TransportMetrics.ErrorTypeOf(null!)).Should().Throw<ArgumentNullException>();
    }

    private sealed class TagRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<Dictionary<string, object?>> _measurements = new();

        public TagRecorder(string instrumentName, string keyTag, string keyValue)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                    && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal)
                )
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, _, tags, _) =>
                {
                    var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var tag in tags)
                    {
                        map[tag.Key] = tag.Value;
                    }

                    if (map.TryGetValue(keyTag, out var value) && Equals(value, keyValue))
                    {
                        _measurements.Enqueue(map);
                    }
                }
            );
            _listener.Start();
        }

        public Dictionary<string, object?> Single() => _measurements.Should().ContainSingle().Subject;

        public void Dispose() => _listener.Dispose();
    }
}
