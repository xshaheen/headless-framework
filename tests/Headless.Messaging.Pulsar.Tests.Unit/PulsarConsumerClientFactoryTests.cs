// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Pulsar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Unit tests for PulsarConsumerClientFactory.
/// Note: The Pulsar.Client types cannot be fully mocked. These tests focus on
/// behavior that can be tested through the IConnectionFactory abstraction.
/// </summary>
public sealed class PulsarConsumerClientFactoryTests : TestBase
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<MessagingPulsarOptions> _options;

    public PulsarConsumerClientFactoryTests()
    {
        _connectionFactory = Substitute.For<IConnectionFactory>();
        _loggerFactory = NullLoggerFactory.Instance;
        _options = Options.Create(new MessagingPulsarOptions { ServiceUrl = "pulsar://localhost:6650" });
    }

    [Fact]
    public async Task should_throw_broker_connection_exception_on_connection_failure()
    {
        // given
        _connectionFactory.RentClient().Throws(new InvalidOperationException("Connection failed"));
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () => await factory.CreateAsync("test-group", 1);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_wrap_inner_exception_in_broker_connection_exception()
    {
        // given
        var innerException = new InvalidOperationException("Connection failed");
        _connectionFactory.RentClient().Throws(innerException);
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () => await factory.CreateAsync("test-group", 1);

        // then
        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().BeSameAs(innerException);
    }

    [Fact]
    public void should_enable_client_logging_when_option_enabled()
    {
        // given
        var options = Options.Create(new MessagingPulsarOptions
        {
            ServiceUrl = "pulsar://localhost:6650",
            EnableClientLog = true,
        });
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(NullLogger.Instance);

        // when
        _ = new PulsarConsumerClientFactory(_connectionFactory, loggerFactory, options);

        // then
        loggerFactory.Received(1).CreateLogger(Arg.Any<string>());
    }

    [Fact]
    public void should_not_enable_client_logging_when_option_disabled()
    {
        // given
        var options = Options.Create(new MessagingPulsarOptions
        {
            ServiceUrl = "pulsar://localhost:6650",
            EnableClientLog = false,
        });
        var loggerFactory = Substitute.For<ILoggerFactory>();

        // when
        _ = new PulsarConsumerClientFactory(_connectionFactory, loggerFactory, options);

        // then
        loggerFactory.DidNotReceive().CreateLogger(Arg.Any<string>());
    }

    [Fact]
    public async Task should_call_rent_client_when_creating_consumer()
    {
        // given - RentClient will throw since we can't mock PulsarClient
        _connectionFactory.RentClient().Throws(new InvalidOperationException("Cannot mock PulsarClient"));
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        try
        {
            await factory.CreateAsync("test-group", 1);
        }
        catch (BrokerConnectionException)
        {
            // Expected
        }

        // then
        _connectionFactory.Received(1).RentClient();
    }
}
