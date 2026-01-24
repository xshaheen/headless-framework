// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.RabbitMq;

namespace Tests;

public sealed class RabbitMqValidationTests : TestBase
{
    [Fact]
    public void should_accept_valid_queue_name()
    {
        // given
        const string validName = "my-queue.name_123";

        // when
        var action = () => RabbitMqValidation.ValidateQueueName(validName);

        // then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_queue_name(string? invalidName)
    {
        // given, When
        var action = () => RabbitMqValidation.ValidateQueueName(invalidName!);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_queue_name_exceeding_max_length()
    {
        // given
        var tooLongName = new string('a', 256);

        // when
        var action = () => RabbitMqValidation.ValidateQueueName(tooLongName);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Theory]
    [InlineData("invalid name")]
    [InlineData("invalid@name")]
    [InlineData("invalid#name")]
    [InlineData("invalid!name")]
    [InlineData("invalid/name")]
    public void should_reject_queue_name_with_invalid_characters(string invalidName)
    {
        // given, When
        var action = () => RabbitMqValidation.ValidateQueueName(invalidName);

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public void should_accept_valid_exchange_name()
    {
        // given
        const string validName = "my-exchange.name_123";

        // when
        var action = () => RabbitMqValidation.ValidateExchangeName(validName);

        // then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_exchange_name(string? invalidName)
    {
        // given, When
        var action = () => RabbitMqValidation.ValidateExchangeName(invalidName!);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_exchange_name_exceeding_max_length()
    {
        // given
        var tooLongName = new string('a', 256);

        // when
        var action = () => RabbitMqValidation.ValidateExchangeName(tooLongName);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Theory]
    [InlineData("invalid name")]
    [InlineData("invalid@name")]
    [InlineData("invalid#name")]
    public void should_reject_exchange_name_with_invalid_characters(string invalidName)
    {
        // given, When
        var action = () => RabbitMqValidation.ValidateExchangeName(invalidName);

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public void should_accept_valid_topic_name()
    {
        // given
        const string validName = "my-topic.name_123";

        // when
        var action = () => RabbitMqValidation.ValidateTopicName(validName);

        // then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_topic_name(string? invalidName)
    {
        // given, When
        var action = () => RabbitMqValidation.ValidateTopicName(invalidName!);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_topic_name_exceeding_max_length()
    {
        // given
        var tooLongName = new string('a', 256);

        // when
        var action = () => RabbitMqValidation.ValidateTopicName(tooLongName);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Theory]
    [InlineData("invalid name")]
    [InlineData("invalid@name")]
    public void should_reject_topic_name_with_invalid_characters(string invalidName)
    {
        // given, When
        var action = () => RabbitMqValidation.ValidateTopicName(invalidName);

        // then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }
}
