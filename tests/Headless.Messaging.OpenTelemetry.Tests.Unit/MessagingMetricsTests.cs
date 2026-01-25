// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Framework.Testing.Tests;
using Headless.Messaging.OpenTelemetry;

namespace Tests;

public sealed class MessagingMetricsTests : TestBase
{
    [Fact]
    public void should_create_meter_with_correct_name()
    {
        // given/when
        using var metrics = new MessagingMetrics();

        // then
        MessagingMetrics.MeterName.Should().Be("Headless.Messaging");
    }

    [Fact]
    public void should_increment_publish_counter()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.publish.messages")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.publish.messages")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordPublish("test.topic", "RabbitMQ");

        // then
        count.Should().Be(1);
    }

    [Fact]
    public void should_record_publish_duration()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.publish.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.publish.duration")
                {
                    duration = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordPublish("test.topic", "RabbitMQ", elapsedMs: 150);

        // then
        duration.Should().Be(150);
    }

    [Fact]
    public void should_increment_publish_error_counter()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.publish.errors")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.publish.errors")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordPublishError("test.topic", "RabbitMQ", "TimeoutException");

        // then
        count.Should().Be(1);
    }

    [Fact]
    public void should_increment_consume_counter()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.consume.messages")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.consume.messages")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordConsume("test.topic", "Kafka", "consumer-group-1");

        // then
        count.Should().Be(1);
    }

    [Fact]
    public void should_record_consume_duration()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.consume.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.consume.duration")
                {
                    duration = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordConsume("test.topic", "Kafka", "consumer-group-1", elapsedMs: 200);

        // then
        duration.Should().Be(200);
    }

    [Fact]
    public void should_increment_consume_error_counter()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.consume.errors")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.consume.errors")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordConsumeError("test.topic", "Kafka", "DeserializationException", "consumer-group-1");

        // then
        count.Should().Be(1);
    }

    [Fact]
    public void should_increment_subscriber_invocation_counter()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.subscriber.invocations")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.subscriber.invocations")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordSubscriberInvocation("HandleOrderCreated", "order.created");

        // then
        count.Should().Be(1);
    }

    [Fact]
    public void should_record_subscriber_duration()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.subscriber.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.subscriber.duration")
                {
                    duration = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordSubscriberInvocation("HandleOrderCreated", "order.created", elapsedMs: 75);

        // then
        duration.Should().Be(75);
    }

    [Fact]
    public void should_increment_subscriber_error_counter()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.subscriber.errors")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.subscriber.errors")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordSubscriberError("HandleOrderCreated", "order.created", "InvalidOperationException");

        // then
        count.Should().Be(1);
    }

    [Fact]
    public void should_record_persistence_duration_for_publish()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        string? persistenceType = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.persistence.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
            {
                if (instrument.Name == "messaging.persistence.duration")
                {
                    duration = measurement;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "messaging.persistence.type")
                        {
                            persistenceType = tag.Value?.ToString();
                        }
                    }
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordPersistence("order.created", 50, isPublish: true);

        // then
        duration.Should().Be(50);
        persistenceType.Should().Be("publish");
    }

    [Fact]
    public void should_record_persistence_duration_for_consume()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        string? persistenceType = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.persistence.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
            {
                if (instrument.Name == "messaging.persistence.duration")
                {
                    duration = measurement;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "messaging.persistence.type")
                        {
                            persistenceType = tag.Value?.ToString();
                        }
                    }
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordPersistence("order.created", 60, isPublish: false);

        // then
        duration.Should().Be(60);
        persistenceType.Should().Be("consume");
    }

    [Fact]
    public void should_record_message_size()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? size = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.message.size")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.message.size")
                {
                    size = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordMessageSize(1024, "order.created");

        // then
        size.Should().Be(1024);
    }

    [Fact]
    public void should_dispose_meter_on_dispose()
    {
        // given
        var metrics = new MessagingMetrics();

        // when
        metrics.Dispose();

        // then - no exception and meter is disposed (verified implicitly)
        // calling dispose again should not throw
        var act = () => metrics.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_record_publish_duration_when_elapsed_is_null()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.publish.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.publish.duration")
                {
                    duration = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordPublish("test.topic", "RabbitMQ", elapsedMs: null);

        // then
        duration.Should().BeNull();
    }

    [Fact]
    public void should_not_record_consume_duration_when_elapsed_is_null()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        double? duration = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.consume.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.consume.duration")
                {
                    duration = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordConsume("test.topic", "Kafka", "group1", elapsedMs: null);

        // then
        duration.Should().BeNull();
    }

    [Fact]
    public void should_consume_without_consumer_group()
    {
        // given
        using var metrics = new MessagingMetrics();
        using var meterListener = new MeterListener();

        long? count = null;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.consume.messages")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "messaging.consume.messages")
                {
                    count = measurement;
                }
            }
        );
        meterListener.Start();

        // when
        metrics.RecordConsume("test.topic", "Kafka");

        // then
        count.Should().Be(1);
    }
}
