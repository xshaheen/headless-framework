// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Unit tests for PulsarConsumerClientFactory.
/// Note: The Pulsar.Client types cannot be fully mocked. These tests focus on
/// behavior that can be tested through the IConnectionFactory abstraction.
/// </summary>
public sealed class PulsarConsumerClientFactoryTests : TestBase
{
    private readonly IConnectionFactory _connectionFactory = Substitute.For<IConnectionFactory>();
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    private readonly IOptions<PulsarMessagingOptions> _options = Options.Create(
        new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650" }
    );

    [Fact]
    public async Task should_throw_broker_connection_exception_on_connection_failure()
    {
        // given
        _connectionFactory
            .RentClientAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () => await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue));

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_wrap_inner_exception_in_broker_connection_exception()
    {
        // given
        var innerException = new InvalidOperationException("Connection failed");
        _connectionFactory.RentClientAsync(Arg.Any<CancellationToken>()).ThrowsAsync(innerException);
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () => await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue));

        // then
        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().BeSameAs(innerException);
    }

    [Fact]
    public void should_enable_client_logging_when_option_enabled()
    {
        // given
        var options = Options.Create(
            new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650", EnableClientLog = true }
        );
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
        var options = Options.Create(
            new PulsarMessagingOptions { ServiceUrl = "pulsar://localhost:6650", EnableClientLog = false }
        );
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
        _connectionFactory
            .RentClientAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Cannot mock PulsarClient"));
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        try
        {
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), AbortToken);
        }
        catch (BrokerConnectionException)
        {
            // Expected
        }

        // then
        await _connectionFactory.Received(1).RentClientAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_propagate_exact_token_without_wrapping_cancellation()
    {
        // given - a live token the rent cancels itself: the factory must pass that exact token down and surface
        // its cancellation unwrapped. (An already-cancelled token no longer reaches the rent at all: the
        // factory observes it before it builds the client.)
        using var cts = new CancellationTokenSource();
        _connectionFactory
            .RentClientAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cts.Cancel();
                return Task.FromCanceled<Pulsar.Client.Api.PulsarClient>(call.Arg<CancellationToken>());
            });
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
        await _connectionFactory.Received(1).RentClientAsync(cts.Token);
    }

    [Fact]
    public async Task should_preserve_factory_cancellation_when_failure_precedes_token_observation()
    {
        // given - the caller cancels while the client rent is in flight, and the rent then fails with its own
        // exception before any await observes the token: the failure must not outrank the cancellation.
        using var cts = new CancellationTokenSource();
        _connectionFactory
            .RentClientAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cts.Cancel();
                return Task.FromException<Pulsar.Client.Api.PulsarClient>(
                    new InvalidOperationException("connect failed")
                );
            });
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
    }
}
