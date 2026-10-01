// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tests;

public sealed class RabbitMqConsumerClientValidationTests : TestBase
{
    private readonly IConnectionChannelPool _pool;
    private readonly IOptions<RabbitMqMessagingOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public RabbitMqConsumerClientValidationTests()
    {
        _pool = Substitute.For<IConnectionChannelPool>();
        _pool.Exchange.Returns("test-exchange");

        _options = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "test_user",
                Password = "test_pass",
            }
        );

        _serviceProvider = Substitute.For<IServiceProvider>();
    }

    [Fact]
    public void should_accept_valid_consumer_identity()
    {
        // given
        const string validGroupName = "valid-queue_name.123";

        // when
        var action = () => new RabbitMqConsumerClient(validGroupName, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_subscription_name(string? groupName)
    {
        // given, When
        var action = () => new RabbitMqConsumerClient(groupName!, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_accept_bus_consumer_identity_with_characters_a_queue_name_rejects()
    {
        // given - the identity is not a queue name; only the queue derived from it must be valid
        const string identity = "billing ops.invoice/projection";

        // when
        var action = () => new RabbitMqConsumerClient(identity, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().NotThrow();
        RabbitMqConsumerClient
            .GetQueueName(identity, "orders.created", MessageLane.Bus)
            .Should()
            .MatchRegex("^bus\\.billing-ops\\.invoice-projection-[0-9a-f]{12}$");
    }

    [Fact]
    public void should_bound_bus_queue_to_max_length_when_consumer_identity_is_too_long()
    {
        // given
        var identity = new string('a', 300);

        // when
        var action = () => new RabbitMqConsumerClient(identity, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().NotThrow();
        RabbitMqConsumerClient.GetQueueName(identity, "orders.created", MessageLane.Bus).Should().HaveLength(255);
    }

    [Fact]
    public async Task should_reject_invalid_topic_name_on_subscribe()
    {
        // given
        await using var client = new RabbitMqConsumerClient("valid-queue", 1, _pool, _options, _serviceProvider);

        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);
        _pool.GetConnectionAsync(AbortToken).Returns(connection);

        var invalidTopics = new[] { "invalid message name" };

        // when
        var action = async () => await client.SubscribeAsync(invalidTopics, AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*alphanumeric*");
        await _pool.DidNotReceive().GetConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_overlong_lane_qualified_topic_before_broker_side_effects()
    {
        await using var client = new RabbitMqConsumerClient(
            "valid-queue",
            1,
            _pool,
            _options,
            _serviceProvider,
            lane: MessageLane.Queue
        );
        var validLogicalButInvalidPhysicalName = new string('a', 252);

        var action = async () => await client.SubscribeAsync([validLogicalButInvalidPhysicalName], AbortToken);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
        await _pool.DidNotReceive().GetConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_accept_valid_topic_name_on_subscribe()
    {
        // given
        await using var client = new RabbitMqConsumerClient("valid-queue", 1, _pool, _options, _serviceProvider);

        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), AbortToken).Returns(channel);
        _pool.GetConnectionAsync(AbortToken).Returns(connection);

        var validTopics = new[] { "valid-messageName.name_123" };

        // when
        var action = async () => await client.SubscribeAsync(validTopics, AbortToken);

        // then
        await action.Should().NotThrowAsync();
    }
}
