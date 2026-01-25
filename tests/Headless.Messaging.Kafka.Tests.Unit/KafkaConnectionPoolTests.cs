// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Framework.Testing.Tests;
using Headless.Messaging.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaConnectionPoolTests : TestBase
{
    private readonly ILogger<KafkaConnectionPool> _logger = NullLogger<KafkaConnectionPool>.Instance;
    private readonly IOptions<MessagingKafkaOptions> _options = Options.Create(
        new MessagingKafkaOptions { Servers = "localhost:9092" }
    );

    [Fact]
    public void should_have_correct_servers_address()
    {
        // given, when
        using var pool = new KafkaConnectionPool(_logger, _options);

        // then
        pool.ServersAddress.Should().Be("localhost:9092");
    }

    [Fact]
    public void should_return_producer_to_pool_when_under_max_size()
    {
        // given
        using var pool = new KafkaConnectionPool(_logger, _options);
        var producer = Substitute.For<IProducer<string, byte[]>>();

        // when
        var result = pool.Return(producer);

        // then
        result.Should().BeTrue();
        producer.DidNotReceive().Dispose();
    }

    [Fact]
    public void should_dispose_producer_when_pool_is_full()
    {
        // given
        var smallPoolOptions = Options.Create(
            new MessagingKafkaOptions { Servers = "localhost:9092", ConnectionPoolSize = 1 }
        );
        using var pool = new KafkaConnectionPool(_logger, smallPoolOptions);

        var producer1 = Substitute.For<IProducer<string, byte[]>>();
        var producer2 = Substitute.For<IProducer<string, byte[]>>();

        // when
        var result1 = pool.Return(producer1);
        var result2 = pool.Return(producer2);

        // then
        result1.Should().BeTrue();
        result2.Should().BeFalse();
        producer1.DidNotReceive().Dispose();
        producer2.Received(1).Dispose();
    }

    [Fact]
    public void should_dispose_all_producers_on_pool_dispose()
    {
        // given
        var pool = new KafkaConnectionPool(_logger, _options);
        var producer1 = Substitute.For<IProducer<string, byte[]>>();
        var producer2 = Substitute.For<IProducer<string, byte[]>>();

        pool.Return(producer1);
        pool.Return(producer2);

        // when
        pool.Dispose();

        // then
        producer1.Received(1).Dispose();
        producer2.Received(1).Dispose();
    }

    [Fact]
    public void should_reuse_producers_from_pool()
    {
        // given
        using var pool = new KafkaConnectionPool(_logger, _options);
        var producer = Substitute.For<IProducer<string, byte[]>>();

        // when
        pool.Return(producer);
        var rentedProducer = pool.RentProducer();

        // then
        rentedProducer.Should().BeSameAs(producer);
    }

    [Fact]
    public void should_create_new_producer_when_pool_is_empty()
    {
        // given - pool with invalid servers to test creation path
        var invalidOptions = Options.Create(
            new MessagingKafkaOptions
            {
                Servers = "invalid-host:9092",
                MainConfig = { ["socket.timeout.ms"] = "100" },
            }
        );
        using var pool = new KafkaConnectionPool(_logger, invalidOptions);

        // when - this creates a producer (Kafka producers don't fail on creation)
        var producer = pool.RentProducer();

        // then
        producer.Should().NotBeNull();
        producer.Dispose();
    }

    [Fact]
    public void should_handle_concurrent_returns()
    {
        // given
        using var pool = new KafkaConnectionPool(_logger, _options);
        var producers = Enumerable.Range(0, 20).Select(_ => Substitute.For<IProducer<string, byte[]>>()).ToList();

        // when
        Parallel.ForEach(producers, producer => pool.Return(producer));

        // then - at least some should be returned successfully, some may be disposed
        var returnedCount = producers.Count(p => !p.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "Dispose"));
        returnedCount.Should().BeLessThanOrEqualTo(10); // max pool size
    }

    [Fact]
    public void should_decrement_count_on_successful_rent()
    {
        // given
        var smallPoolOptions = Options.Create(
            new MessagingKafkaOptions { Servers = "localhost:9092", ConnectionPoolSize = 2 }
        );
        using var pool = new KafkaConnectionPool(_logger, smallPoolOptions);

        var producer1 = Substitute.For<IProducer<string, byte[]>>();
        var producer2 = Substitute.For<IProducer<string, byte[]>>();

        pool.Return(producer1);
        pool.Return(producer2);

        // when - rent both producers
        var rented1 = pool.RentProducer();
        var rented2 = pool.RentProducer();

        // then - can return both again since count decremented
        var canReturn1 = pool.Return(rented1);
        var canReturn2 = pool.Return(rented2);

        canReturn1.Should().BeTrue();
        canReturn2.Should().BeTrue();
    }
}
