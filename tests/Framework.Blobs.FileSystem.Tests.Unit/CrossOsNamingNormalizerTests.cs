// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;

namespace Tests;

public sealed class CrossOsNamingNormalizerTests
{
    private readonly CrossOsNamingNormalizer _sut = new();

    [Theory]
    [InlineData("my container", "my container")]
    [InlineData("container name with spaces", "container name with spaces")]
    public void should_preserve_spaces_in_container_name(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my.container", "my.container")]
    [InlineData("file.name.txt", "file.name.txt")]
    public void should_preserve_dots_in_container_name(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("my_container", "my_container")]
    [InlineData("container_with_underscores", "container_with_underscores")]
    public void should_preserve_underscores_in_container_name(string input, string expected)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("/*?<>:|\"\\")]
    [InlineData("/:*?\"<>|\\")]
    public void should_handle_empty_result_after_normalization(string input)
    {
        var result = _sut.NormalizeContainerName(input);

        result.Should().BeEmpty();
    }

    [Fact]
    public void should_handle_unicode_characters()
    {
        // Chinese characters
        _sut.NormalizeContainerName("\u6587\u4EF6\u5939").Should().Be("\u6587\u4EF6\u5939");

        // Arabic characters
        _sut.NormalizeContainerName("\u0645\u0644\u0641").Should().Be("\u0645\u0644\u0641");

        // Emoji characters
        _sut.NormalizeContainerName("\U0001F4C1folder").Should().Be("\U0001F4C1folder");
    }
}
