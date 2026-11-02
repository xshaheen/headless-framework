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
        _connectionFactory.RentClient().Throws(new InvalidOperationException("Connection failed"));
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
        _connectionFactory.RentClient().Throws(innerException);
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
        // given
        _connectionFactory.RentClient().Throws(new InvalidOperationException("Connection failed"));
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
        _connectionFactory.Received(1).RentClient();
    }

    [Fact]
    public async Task should_not_reach_the_broker_when_the_caller_already_canceled()
    {
        // given
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
        _connectionFactory.DidNotReceive().RentClient();
    }

    [Fact]
    public async Task should_preserve_factory_cancellation_when_failure_precedes_token_observation()
    {
        // given - the caller cancels while the client rent is in flight, and the rent then fails with its own
        // exception: the failure must not outrank the cancellation.
        using var cts = new CancellationTokenSource();
        _connectionFactory
            .RentClient()
            .Returns(_ =>
            {
                cts.Cancel();
                throw new InvalidOperationException("connect failed");
            });
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task should_create_a_client_without_opening_a_subscription()
    {
        // given
        _connectionFactory.RentClient().Returns(Substitute.For<DotPulsar.Abstractions.IPulsarClient>());
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options);

        // when
        await using var client = await factory.CreateAsync(
            new ConsumerClientRequest("test-group", 1, MessageLane.Queue),
            AbortToken
        );

        // then
        client.Should().BeOfType<PulsarConsumerClient>();
        _connectionFactory.DidNotReceive().GetProducer(Arg.Any<string>());
    }

    [Fact]
    public async Task should_reject_key_shared_for_an_every_instance_consumer_before_reaching_the_broker()
    {
        // given
        var registry = _Registry(MessageLane.Bus, "orders.audit", new PulsarConsumerConfig(KeyShared: true));
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options, registry);
        var request = new ConsumerClientRequest(
            "orders.audit",
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            Guid.NewGuid()
        );

        // when
        var act = async () => await factory.CreateAsync(request, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*every-instance*Key_Shared*");
        _connectionFactory.DidNotReceive().RentClient();
    }

    [Fact]
    public async Task should_reject_conflicting_pulsar_options_on_one_subscription_before_reaching_the_broker()
    {
        // given
        var registry = _Registry(
            MessageLane.Queue,
            "orders.placed",
            new PulsarConsumerConfig(KeyShared: true),
            new PulsarConsumerConfig(KeyShared: false, MaxRedeliveryCount: 3)
        );
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options, registry);

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("orders.placed", 1, MessageLane.Queue), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*conflicting*");
        _connectionFactory.DidNotReceive().RentClient();
    }

    [Fact]
    public async Task should_reach_the_broker_when_a_competing_consumer_uses_key_shared()
    {
        // given
        var registry = _Registry(MessageLane.Queue, "orders.placed", new PulsarConsumerConfig(KeyShared: true));
        _connectionFactory.RentClient().Throws(new InvalidOperationException("Connection failed"));
        var factory = new PulsarConsumerClientFactory(_connectionFactory, _loggerFactory, _options, registry);

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("orders.placed", 1, MessageLane.Queue), AbortToken);

        // then
        await act.Should().ThrowAsync<BrokerConnectionException>();
        _connectionFactory.Received(1).RentClient();
    }

    private static IConsumerRegistry _Registry(MessageLane lane, string name, params PulsarConsumerConfig[] configs)
    {
        var registry = Substitute.For<IConsumerRegistry>();
        registry
            .GetAll()
            .Returns(
                configs
                    .Select(
                        (config, index) =>
                            new ConsumerMetadata(
                                typeof(object),
                                typeof(object),
                                name,
                                1,
                                lane,
                                lane == MessageLane.Bus ? name : $"{name}.consumer-{index}",
                                "v1"
                            )
                            {
                                ProviderConfigs = new Dictionary<Type, object>
                                {
                                    [typeof(PulsarConsumerConfig)] = config,
                                },
                            }
                    )
                    .ToArray()
            );

        return registry;
    }
}
