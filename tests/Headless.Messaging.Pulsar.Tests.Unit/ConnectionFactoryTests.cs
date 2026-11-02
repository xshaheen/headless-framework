// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using DotPulsar.Abstractions;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ConnectionFactoryTests : TestBase
{
    private readonly ILogger<ConnectionFactory> _logger = NullLogger<ConnectionFactory>.Instance;

    private readonly IOptions<PulsarMessagingOptions> _options = Options.Create(
        new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }
    );

    [Fact]
    public async Task should_have_correct_servers_address()
    {
        // given
        await using var factory = new ConnectionFactory(_logger, _options);

        // when
        var address = factory.ServersAddress;

        // then
        address.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_sanitize_servers_address_when_credentials_are_present()
    {
        // given
        var credentialedOptions = Options.Create(
            new PulsarMessagingOptions { ServiceUrl = "pulsar://user:secret@localhost:6650" }
        );

        // when
        await using var factory = new ConnectionFactory(_logger, credentialedOptions);

        // then
        factory.ServersAddress.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_build_one_client_and_hand_it_to_every_caller()
    {
        // given
        await using var factory = new ConnectionFactory(_logger, _options);

        // when: building the client opens no connection
        var first = factory.RentClient();
        var second = factory.RentClient();

        // then
        first.Should().BeSameAs(second);
        first.ServiceUrl.Should().Be(new Uri("pulsar://localhost:6650"));
    }

    [Fact]
    public async Task should_refuse_a_client_after_dispose()
    {
        // given
        var factory = new ConnectionFactory(_logger, _options);
        await factory.DisposeAsync();

        // when
        var act = () => factory.RentClient();

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        await using var factory = new ConnectionFactory(_logger, _options);

        // when
        var act = async () => await factory.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_reuse_the_producer_of_a_topic_while_it_is_live()
    {
        // given
        var created = 0;
        await using var factory = new ConnectionFactory(
            _logger,
            _options,
            _ =>
            {
                Interlocked.Increment(ref created);
                return _Producer(final: false);
            }
        );

        // when
        var first = factory.GetProducer("orders.created");
        var second = factory.GetProducer("orders.created");

        // then
        first.Should().BeSameAs(second);
        created.Should().Be(1);
    }

    [Fact]
    public async Task should_replace_a_producer_that_faulted()
    {
        // given: a faulted DotPulsar producer never recovers on its own
        var faulted = _Producer(final: true);
        var replacement = _Producer(final: false);
        var producers = new Queue<IProducer<ReadOnlySequence<byte>>>([faulted, replacement]);
        await using var factory = new ConnectionFactory(_logger, _options, _ => producers.Dequeue());

        // when
        var producer = factory.GetProducer("orders.created");

        // then
        producer.Should().BeSameAs(replacement);
        await faulted.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_expose_the_configured_send_timeout()
    {
        // given
        var options = Options.Create(
            new PulsarMessagingOptions
            {
                ServiceUrl = "pulsar://localhost:6650",
                Producer = { SendTimeout = TimeSpan.FromSeconds(5) },
            }
        );

        // when
        await using var factory = new ConnectionFactory(_logger, options);

        // then
        factory.SendTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    private static IProducer<ReadOnlySequence<byte>> _Producer(bool final)
    {
        var producer = Substitute.For<IProducer<ReadOnlySequence<byte>>>();
        producer.State.IsFinalState().Returns(final);
        return producer;
    }
}
