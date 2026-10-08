// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging.Kafka;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaProducerProviderTests : TestBase
{
    private readonly ILogger<KafkaProducerProvider> _logger = NullLogger<KafkaProducerProvider>.Instance;

    private readonly IOptions<KafkaMessagingOptions> _options = Options.Create(
        new KafkaMessagingOptions { Servers = "localhost:9092" }
    );

    [Fact]
    public void should_have_correct_servers_address()
    {
        // given, when
        using var provider = new KafkaProducerProvider(_logger, _options);

        // then
        provider.ServersAddress.Should().Be("localhost:9092");
    }

    [Fact]
    public void should_sanitize_servers_address_when_credentials_are_present()
    {
        // given
        var credentialedOptions = Options.Create(new KafkaMessagingOptions { Servers = "user:secret@broker:9092" });

        // when
        using var provider = new KafkaProducerProvider(_logger, credentialedOptions);

        // then
        provider.ServersAddress.Should().Be("broker:9092");
    }

    [Fact]
    public void should_enable_idempotence_and_keep_librdkafka_queue_default_when_not_configured()
    {
        // when
        var config = KafkaProducerProvider.BuildConfig(new KafkaMessagingOptions { Servers = "localhost:9092" });

        // then
        config.BootstrapServers.Should().Be("localhost:9092");
        config.EnableIdempotence.Should().BeTrue();
        config.QueueBufferingMaxMessages.Should().BeNull();
        config.MessageTimeoutMs.Should().Be(5000);
        config.RequestTimeoutMs.Should().Be(3000);
    }

    [Fact]
    public void should_keep_idempotence_disabled_when_main_config_disables_it()
    {
        // given
        var options = new KafkaMessagingOptions
        {
            Servers = "localhost:9092",
            MainConfig = { ["enable.idempotence"] = "false" },
        };

        // when
        var config = KafkaProducerProvider.BuildConfig(options);

        // then
        config.EnableIdempotence.Should().BeFalse();
    }

    [Theory]
    [InlineData("acks", "1")]
    [InlineData("acks", "0")]
    [InlineData("max.in.flight.requests.per.connection", "10")]
    [InlineData("max.in.flight", "6")]
    [InlineData("request.required.acks", "1")]
    [InlineData("retries", "0")]
    [InlineData("message.send.max.retries", "0")]
    [InlineData("queuing.strategy", "lifo")]
    public void should_not_enable_idempotence_when_main_config_conflicts_with_it(string key, string value)
    {
        // given
        var options = new KafkaMessagingOptions { Servers = "localhost:9092", MainConfig = { [key] = value } };

        // when
        var config = KafkaProducerProvider.BuildConfig(options);

        // then
        config.EnableIdempotence.Should().BeNull();
    }

    [Fact]
    public void should_enable_idempotence_when_main_config_sets_compatible_acks_and_in_flight_limit()
    {
        // given
        var options = new KafkaMessagingOptions
        {
            Servers = "localhost:9092",
            MainConfig = { ["acks"] = "all", ["max.in.flight.requests.per.connection"] = "5" },
        };

        // when
        var config = KafkaProducerProvider.BuildConfig(options);

        // then
        config.EnableIdempotence.Should().BeTrue();
    }

    [Fact]
    public void should_keep_main_config_overrides_for_queue_and_timeouts()
    {
        // given
        var options = new KafkaMessagingOptions
        {
            Servers = "localhost:9092",
            MainConfig =
            {
                ["queue.buffering.max.messages"] = "500",
                ["message.timeout.ms"] = "20000",
                ["request.timeout.ms"] = "10000",
            },
        };

        // when
        var config = KafkaProducerProvider.BuildConfig(options);

        // then
        config.QueueBufferingMaxMessages.Should().Be(500);
        config.MessageTimeoutMs.Should().Be(20000);
        config.RequestTimeoutMs.Should().Be(10000);
    }

    [Fact]
    public void should_build_one_producer_and_share_it_across_concurrent_callers()
    {
        // given
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var builds = 0;
        using var provider = new KafkaProducerProvider(
            _logger,
            _options,
            _ =>
            {
                Interlocked.Increment(ref builds);
                return producer;
            }
        );

        // when
        var producers = new IProducer<string, byte[]>[32];
        Parallel.For(0, producers.Length, i => producers[i] = provider.GetProducer());

        // then
        builds.Should().Be(1);
        producers.Should().OnlyContain(p => ReferenceEquals(p, producer));
    }

    [Fact]
    public void should_retry_building_the_producer_after_a_failed_build()
    {
        // given
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var attempts = 0;
        using var provider = new KafkaProducerProvider(
            _logger,
            _options,
            _ => ++attempts == 1 ? throw new KafkaException(ErrorCode.Local_Fail) : producer
        );

        // when
        var first = () => provider.GetProducer();

        // then
        first.Should().Throw<KafkaException>();
        provider.GetProducer().Should().BeSameAs(producer);
        attempts.Should().Be(2);
    }

    [Fact]
    public void should_build_a_new_producer_after_the_failed_one_is_discarded()
    {
        // given
        var failed = Substitute.For<IProducer<string, byte[]>>();
        var replacement = Substitute.For<IProducer<string, byte[]>>();
        var producers = new Queue<IProducer<string, byte[]>>([failed, replacement]);
        using var provider = new KafkaProducerProvider(_logger, _options, _ => producers.Dequeue());
        provider.GetProducer().Should().BeSameAs(failed);

        // when
        provider.DiscardFailedProducer(failed);

        // then
        failed.Received(1).Dispose();
        provider.GetProducer().Should().BeSameAs(replacement);
    }

    [Fact]
    public void should_keep_the_current_producer_when_a_stale_one_is_discarded()
    {
        // given
        var current = Substitute.For<IProducer<string, byte[]>>();
        var stale = Substitute.For<IProducer<string, byte[]>>();
        using var provider = new KafkaProducerProvider(_logger, _options, _ => current);
        provider.GetProducer();

        // when
        provider.DiscardFailedProducer(stale);

        // then
        stale.DidNotReceive().Dispose();
        current.DidNotReceive().Dispose();
        provider.GetProducer().Should().BeSameAs(current);
    }

    [Fact]
    public void should_flush_within_the_message_timeout_then_dispose_the_producer_on_dispose()
    {
        // given
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var options = Options.Create(
            new KafkaMessagingOptions { Servers = "localhost:9092", MainConfig = { ["message.timeout.ms"] = "7000" } }
        );
        var provider = new KafkaProducerProvider(_logger, options, _ => producer);
        provider.GetProducer();

        // when
        provider.Dispose();
        provider.Dispose();

        // then
        Received.InOrder(() =>
        {
            producer.Flush(TimeSpan.FromMilliseconds(7000));
            producer.Dispose();
        });
        producer.Received(1).Dispose();
    }

    [Fact]
    public void should_dispose_the_producer_when_flush_fails()
    {
        // given
        var producer = Substitute.For<IProducer<string, byte[]>>();
        producer.Flush(Arg.Any<TimeSpan>()).Returns(_ => throw new KafkaException(ErrorCode.Local_Fail));
        using var provider = new KafkaProducerProvider(_logger, _options, _ => producer);
        provider.GetProducer();

        // when
        var act = provider.Dispose;

        // then
        act.Should().NotThrow();
        producer.Received(1).Dispose();
    }

    [Fact]
    public void should_dispose_when_flush_leaves_records_undelivered()
    {
        // given
        var producer = Substitute.For<IProducer<string, byte[]>>();
        producer.Flush(Arg.Any<TimeSpan>()).Returns(3);
        var provider = new KafkaProducerProvider(_logger, _options, _ => producer);
        provider.GetProducer();

        // when
        provider.Dispose();

        // then
        producer.Received(1).Dispose();
    }

    [Fact]
    public void should_reject_get_producer_after_dispose()
    {
        // given
        var provider = new KafkaProducerProvider(_logger, _options, _ => Substitute.For<IProducer<string, byte[]>>());
        provider.Dispose();

        // when
        var act = () => provider.GetProducer();

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void should_build_a_real_producer_without_contacting_the_broker()
    {
        // given
        var options = Options.Create(
            new KafkaMessagingOptions { Servers = "invalid-host:9092", MainConfig = { ["socket.timeout.ms"] = "100" } }
        );
        using var provider = new KafkaProducerProvider(_logger, options);

        // when
        var producer = provider.GetProducer();

        // then
        producer.Should().NotBeNull();
        provider.GetProducer().Should().BeSameAs(producer);
    }
}
