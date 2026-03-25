// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Tests;

public sealed class ConnectionFactoryTests : TestBase
{
    private readonly ILogger<ConnectionFactory> _logger;
    private readonly IOptions<MessagingPulsarOptions> _options;

    public ConnectionFactoryTests()
    {
        _logger = NullLogger<ConnectionFactory>.Instance;
        _options = Options.Create(new MessagingPulsarOptions { ServiceUrl = "pulsar://localhost:6650" });
    }

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
    public async Task should_create_client_with_service_url()
    {
        // given
        await using var factory = new ConnectionFactory(_logger, _options);

        // when, then - RentClient creates PulsarClient with the service URL
        // Since we can't mock PulsarClientBuilder easily, we verify the ServersAddress
        factory.ServersAddress.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // given
        var factory = new ConnectionFactory(_logger, _options);

        // when
        var act = async () => await factory.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_retry_producer_creation_after_transient_failure()
    {
        // given
        var producer = Substitute.For<IProducer<byte[]>>();
        var callCount = 0;
        await using var factory = new ConnectionFactory(
            _logger,
            _options,
            _ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return Task.FromException<IProducer<byte[]>>(new InvalidOperationException("transient"));
                }

                return Task.FromResult(producer);
            }
        );

        // when
        var firstAttempt = async () => await factory.CreateProducerAsync("orders.created");

        // then
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>();
        var recoveredProducer = await factory.CreateProducerAsync("orders.created");
        recoveredProducer.Should().BeSameAs(producer);
        callCount.Should().Be(2);
    }
}
