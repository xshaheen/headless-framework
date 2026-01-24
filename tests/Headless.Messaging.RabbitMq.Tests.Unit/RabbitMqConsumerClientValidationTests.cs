// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.RabbitMq;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class RabbitMqConsumerClientValidationTests : TestBase
{
    private readonly IConnectionChannelPool _pool;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    public RabbitMqConsumerClientValidationTests()
    {
        _pool = Substitute.For<IConnectionChannelPool>();
        _pool.Exchange.Returns("test-exchange");

        _options = Options.Create(new RabbitMqOptions { HostName = "localhost", Port = 5672 });

        _serviceProvider = Substitute.For<IServiceProvider>();
    }

    [Fact]
    public void should_accept_valid_group_name()
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
    public void should_reject_null_or_whitespace_group_name(string? groupName)
    {
        // given, When
        var action = () => new RabbitMqConsumerClient(groupName!, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_group_name_with_invalid_characters()
    {
        // given
        const string invalidGroupName = "invalid queue name";

        // when
        var action = () => new RabbitMqConsumerClient(invalidGroupName, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public void should_reject_group_name_exceeding_max_length()
    {
        // given
        var tooLongName = new string('a', 256);

        // when
        var action = () => new RabbitMqConsumerClient(tooLongName, 1, _pool, _options, _serviceProvider);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Fact]
    public async Task should_reject_invalid_topic_name_on_subscribe()
    {
        // given
        await using var client = new RabbitMqConsumerClient("valid-queue", 1, _pool, _options, _serviceProvider);

        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>()).Returns(channel);
        _pool.GetConnectionAsync().Returns(connection);

        var invalidTopics = new[] { "invalid topic name" };

        // when
        var action = async () => await client.SubscribeAsync(invalidTopics);

        // then
        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public async Task should_accept_valid_topic_name_on_subscribe()
    {
        // given
        await using var client = new RabbitMqConsumerClient("valid-queue", 1, _pool, _options, _serviceProvider);

        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), AbortToken).Returns(channel);
        _pool.GetConnectionAsync().Returns(connection);

        var validTopics = new[] { "valid-topic.name_123" };

        // when
        var action = async () => await client.SubscribeAsync(validTopics);

        // then
        await action.Should().NotThrowAsync();
    }
}
