// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AwsSqs;

namespace Tests;

public sealed class TopicNormalizerTests
{
    [Fact]
    public void should_replace_dots_with_dashes()
    {
        // given
        const string input = "my.topic.name";

        // when
        var result = input.NormalizeForAws();

        // then
        result.Should().Be("my-topic-name");
    }

    [Fact]
    public void should_replace_colons_with_underscores()
    {
        // given
        const string input = "my:topic:name";

        // when
        var result = input.NormalizeForAws();

        // then
        result.Should().Be("my_topic_name");
    }

    [Fact]
    public void should_replace_both_dots_and_colons()
    {
        // given
        const string input = "my.topic:name.test";

        // when
        var result = input.NormalizeForAws();

        // then
        result.Should().Be("my-topic_name-test");
    }

    [Fact]
    public void should_accept_256_character_topic_name()
    {
        // given - AWS SNS max is 256 chars
        var input = new string('a', 256);

        // when
        var result = input.NormalizeForAws();

        // then
        result.Should().HaveLength(256);
    }

    [Fact]
    public void should_accept_valid_length_topic_name()
    {
        // given
        const string input = "short-string";

        // when
        var result = input.NormalizeForAws();

        // then
        result.Should().Be("short-string");
    }

    [Fact]
    public void should_throw_when_length_exceeds_256_chars()
    {
        // given
        var input = new string('a', 257);

        // when
        var testCode = () => input.NormalizeForAws();

        // then
        testCode
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("*AWS SNS topic names must be 256 characters or less*");
    }

    [Fact]
    public void should_throw_when_much_longer_than_256_chars()
    {
        // given
        var input = new string('a', 300);

        // when
        var testCode = () => input.NormalizeForAws();

        // then
        testCode.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_on_empty_string()
    {
        // given
        var input = string.Empty;

        // when
        var testCode = () => input.NormalizeForAws();

        // then
        testCode.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void should_throw_on_null_string()
    {
        // given
#pragma warning disable RCS1118
        string input = null!;
#pragma warning restore RCS1118

        // when
        var testCode = () => input.NormalizeForAws();

        // then
        testCode.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_on_whitespace_only_string()
    {
        // given
        const string input = "   ";

        // when
        var testCode = () => input.NormalizeForAws();

        // then
        testCode.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void should_preserve_other_special_characters()
    {
        // given
        const string input = "-my_topic";

        // when
        var result = input.NormalizeForAws();

        // then
        result.Should().Be("-my_topic");
    }
}
