// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
using Framework.Checks;
using Framework.Messages;
using Xunit.v3;

namespace Tests;

public sealed class TopicNormalizerTests
{
    [Fact]
    public void should_replace_dots_with_dashes()
    {
        // Arrange
        var input = "my.topic.name";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Be("my-topic-name");
    }

    [Fact]
    public void should_replace_colons_with_underscores()
    {
        // Arrange
        var input = "my:topic:name";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Be("my_topic_name");
    }

    [Fact]
    public void should_replace_both_dots_and_colons()
    {
        // Arrange
        var input = "my.topic:name.test";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Be("my-topic_name-test");
    }

    [Fact]
    public void should_accept_256_character_topic_name()
    {
        // Arrange - AWS SNS max is 256 chars
        var input = new string('a', 256);

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().HaveLength(256);
    }

    [Fact]
    public void should_accept_valid_length_topic_name()
    {
        // Arrange
        var input = "short-string";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Be("short-string");
    }

    [Fact]
    public void should_throw_when_length_exceeds_256_chars()
    {
        // Arrange
        var input = new string('a', 257);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
        exception.Message.Should().Contain("AWS SNS topic names must be 256 characters or less");
    }

    [Fact]
    public void should_throw_when_much_longer_than_256_chars()
    {
        // Arrange
        var input = new string('a', 300);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_on_empty_string()
    {
        // Arrange
        var input = string.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_on_null_string()
    {
        // Arrange
        string input = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_on_whitespace_only_string()
    {
        // Arrange
        var input = "   ";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_preserve_other_special_characters()
    {
        // Arrange
        var input = "-my_topic";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Be("-my_topic");
    }
}
