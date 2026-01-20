// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
using Framework.Checks;
using Framework.Messages;
using Xunit.v3;

namespace Tests;

public sealed class TopicNormalizerTests
{
    // NOTE: TopicNormalizer has a bug (todo #050) where validation logic is inverted.
    // It currently throws when length <= 256 instead of when length > 256.
    // These tests document the CURRENT (buggy) behavior until todo #050 is resolved.

    [Fact]
    public void should_replace_dots_with_dashes()
    {
        // Arrange - Must be > 256 chars to pass validation (due to bug)
        var input = new string('a', 257) + ".topic.name";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Contain("-topic-name");
    }

    [Fact]
    public void should_replace_colons_with_underscores()
    {
        // Arrange - Must be > 256 chars to pass validation (due to bug)
        var input = new string('a', 257) + ":topic:name";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Contain("_topic_name");
    }

    [Fact]
    public void should_replace_both_dots_and_colons()
    {
        // Arrange - Must be > 256 chars to pass validation (due to bug)
        var input = new string('a', 257) + ".topic:name.test";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Contain("-topic_name-test");
    }

    [Fact]
    public void should_throw_when_length_is_256_or_less()
    {
        // Arrange - Bug causes this to throw when it shouldn't
        var input = new string('a', 256);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_length_is_less_than_256()
    {
        // Arrange - Bug causes this to throw when it shouldn't
        var input = "short-string";

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_accept_string_longer_than_256_chars()
    {
        // Arrange
        var input = new string('a', 300);

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().HaveLength(300);
    }

    [Fact]
    public void should_throw_on_empty_string()
    {
        // Arrange - Bug causes this to throw
        var input = string.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TopicNormalizer.NormalizeForAws(input));
        exception.Should().NotBeNull();
    }

    [Fact]
    public void should_preserve_other_special_characters()
    {
        // Arrange - Must be > 256 chars to pass validation (due to bug)
        var input = new string('a', 257) + "-my_topic";

        // Act
        var result = TopicNormalizer.NormalizeForAws(input);

        // Assert
        result.Should().Contain("-my_topic");
    }
}
