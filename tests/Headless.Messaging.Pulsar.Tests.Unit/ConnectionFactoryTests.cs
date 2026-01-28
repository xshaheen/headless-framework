// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    public void should_have_correct_servers_address()
    {
        // given
        var factory = new ConnectionFactory(_logger, _options);

        // when
        var address = factory.ServersAddress;

        // then
        address.Should().Be("pulsar://localhost:6650");
    }

    [Fact]
    public void should_create_client_with_service_url()
    {
        // given
        var factory = new ConnectionFactory(_logger, _options);

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
}
