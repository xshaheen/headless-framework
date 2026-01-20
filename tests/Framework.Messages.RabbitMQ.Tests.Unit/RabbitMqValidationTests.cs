// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Testing.Tests;

namespace Tests;

public sealed class RabbitMqValidationTests : TestBase
{
    [Fact]
    public void should_accept_valid_queue_name()
    {
        // Given
        var validName = "my-queue.name_123";

        // When
        var action = () => RabbitMqValidation.ValidateQueueName(validName);

        // Then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_queue_name(string? invalidName)
    {
        // Given, When
        var action = () => RabbitMqValidation.ValidateQueueName(invalidName!);

        // Then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_queue_name_exceeding_max_length()
    {
        // Given
        var tooLongName = new string('a', 256);

        // When
        var action = () => RabbitMqValidation.ValidateQueueName(tooLongName);

        // Then
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
        // Given, When
        var action = () => RabbitMqValidation.ValidateQueueName(invalidName);

        // Then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public void should_accept_valid_exchange_name()
    {
        // Given
        var validName = "my-exchange.name_123";

        // When
        var action = () => RabbitMqValidation.ValidateExchangeName(validName);

        // Then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_exchange_name(string? invalidName)
    {
        // Given, When
        var action = () => RabbitMqValidation.ValidateExchangeName(invalidName!);

        // Then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_exchange_name_exceeding_max_length()
    {
        // Given
        var tooLongName = new string('a', 256);

        // When
        var action = () => RabbitMqValidation.ValidateExchangeName(tooLongName);

        // Then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Theory]
    [InlineData("invalid name")]
    [InlineData("invalid@name")]
    [InlineData("invalid#name")]
    public void should_reject_exchange_name_with_invalid_characters(string invalidName)
    {
        // Given, When
        var action = () => RabbitMqValidation.ValidateExchangeName(invalidName);

        // Then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }

    [Fact]
    public void should_accept_valid_topic_name()
    {
        // Given
        var validName = "my-topic.name_123";

        // When
        var action = () => RabbitMqValidation.ValidateTopicName(validName);

        // Then
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_null_or_whitespace_topic_name(string? invalidName)
    {
        // Given, When
        var action = () => RabbitMqValidation.ValidateTopicName(invalidName!);

        // Then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_topic_name_exceeding_max_length()
    {
        // Given
        var tooLongName = new string('a', 256);

        // When
        var action = () => RabbitMqValidation.ValidateTopicName(tooLongName);

        // Then
        action.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed 255 characters*");
    }

    [Theory]
    [InlineData("invalid name")]
    [InlineData("invalid@name")]
    public void should_reject_topic_name_with_invalid_characters(string invalidName)
    {
        // Given, When
        var action = () => RabbitMqValidation.ValidateTopicName(invalidName);

        // Then
        action.Should().Throw<ArgumentException>().WithMessage("*alphanumeric*");
    }
}
