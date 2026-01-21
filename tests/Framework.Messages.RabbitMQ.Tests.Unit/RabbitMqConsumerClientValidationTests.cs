// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;

namespace Tests;

public sealed class RabbitMqConsumerClientValidationTests : TestBase
{
    private readonly IConnectionChannelPool _pool;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public RabbitMqConsumerClientValidationTests()
    {
        _pool = Substitute.For<IConnectionChannelPool>();
        _pool.Exchange.Returns("test-exchange");

        _options = Options.Create(new RabbitMQOptions { HostName = "localhost", Port = 5672 });

        _serviceProvider = Substitute.For<IServiceProvider>();
    }

    [Fact]
    public void should_accept_valid_group_name()
    {
        // Given
        var validGroupName = "valid-queue_name.123";

        // When
        var action = () => new RabbitMqConsumerClient(validGroupName, 1, _pool, _options, _serviceProvider);

        // Then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_group_name(string? groupName)
    {
        // Given, When
        var action = () => new RabbitMqConsumerClient(groupName!, 1, _pool, _options, _serviceProvider);

        // Then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_group_name_with_invalid_characters()
    {
        // Given
        var invalidGroupName = "invalid queue name";

        // When
        var action = () => new RabbitMqConsumerClient(invalidGroupName, 1, _pool, _options, _serviceProvider);

        // Then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public void should_reject_group_name_exceeding_max_length()
    {
        // Given
        var tooLongName = new string('a', 256);

        // When
        var action = () => new RabbitMqConsumerClient(tooLongName, 1, _pool, _options, _serviceProvider);

        // Then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Fact]
    public async Task should_reject_invalid_topic_name_on_subscribe()
    {
        // Given
        var client = new RabbitMqConsumerClient("valid-queue", 1, _pool, _options, _serviceProvider);

        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>()).Returns(channel);
        _pool.GetConnectionAsync().Returns(connection);

        var invalidTopics = new[] { "invalid topic name" };

        // When
        var action = async () => await client.SubscribeAsync(invalidTopics);

        // Then
        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public async Task should_accept_valid_topic_name_on_subscribe()
    {
        // Given
        var client = new RabbitMqConsumerClient("valid-queue", 1, _pool, _options, _serviceProvider);

        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>()).Returns(channel);
        _pool.GetConnectionAsync().Returns(connection);

        var validTopics = new[] { "valid-topic.name_123" };

        // When
        var action = async () => await client.SubscribeAsync(validTopics);

        // Then
        await action.Should().NotThrowAsync();
    }
}
