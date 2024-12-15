// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.IO;

public sealed class StreamExtensionsTests
{
    #region Get All Text

    [Fact]
    public void should_return_correct_text()
    {
        // given
        const string text = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = new MemoryStream(bytes);
        stream.Position = Random.Shared.Next(0, bytes.Length);

        // when
        var result = stream.GetAllText();

        // then
        result.Should().Be(text);
        stream.Position.Should().Be(text.Length);
    }

    [Fact]
    public async Task should_return_correct_text_async()
    {
        // given
        const string text = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(text);
        await using var stream = new MemoryStream(bytes);
        stream.Position = Random.Shared.Next(0, bytes.Length);

        // when
        var result = await stream.GetAllTextAsync();

        // then
        result.Should().Be(text);
        stream.Position.Should().Be(text.Length);
    }

    #endregion

    #region Get All Bytes

    [Fact]
    public void should_return_correct_bytes()
    {
        // given
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        using var stream = new MemoryStream(bytes);
        stream.Position = Random.Shared.Next(0, bytes.Length);

        // when
        var result = stream.GetAllBytes();

        // then
        result.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task should_return_correct_bytes_async()
    {
        // given
        const string text = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(text);
        await using var stream = new MemoryStream(bytes);

        // when
        var result = await stream.GetAllBytesAsync();

        // then
        result.Should().BeEquivalentTo(bytes);
    }

    #endregion

    #region Write Text

    [Fact]
    public void should_write_text_to_stream()
    {
        // given
        const string text = "Hello, World!";
        using var stream = new MemoryStream();

        // when
        stream.WriteText(text);

        // then
        var resultText = stream.GetAllText();
        resultText.Should().Be(text);
        stream.Position.Should().Be(Encoding.UTF8.GetByteCount(text));
    }

    [Fact]
    public async Task should_write_text_to_stream_async()
    {
        // given
        const string text = "Hello, World!";
        await using var stream = new MemoryStream();

        // when
        await stream.WriteTextAsync(text);

        // then
        var resultText = await stream.GetAllTextAsync();
        resultText.Should().Be(text);
        stream.Position.Should().Be(Encoding.UTF8.GetByteCount(text));
    }

    #endregion

    #region Create Memory Stream

    [Fact]
    public void should_create_memory_stream()
    {
        // given
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        using var stream = new MemoryStream(bytes);
        stream.Position = Random.Shared.Next(0, bytes.Length); // It should not affect the result

        // when
        using var memoryStream = stream.CreateMemoryStream();

        // then
        memoryStream.Should().NotBeNull();
        memoryStream.Position.Should().Be(bytes.Length);
        memoryStream.Length.Should().Be(stream.Length);
        memoryStream.ToArray().Should().BeEquivalentTo(bytes);

        // assert original stream is not changed
        stream.Position.Should().Be(bytes.Length);
        stream.Length.Should().Be(bytes.Length);
        stream.ToArray().Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task should_create_memory_stream_async()
    {
        // given
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        await using var stream = new MemoryStream(bytes);
        stream.Position = Random.Shared.Next(0, bytes.Length); // It should not affect the result

        // when
        await using var memoryStream = await stream.CreateMemoryStreamAsync();

        // then
        memoryStream.Should().NotBeNull();
        memoryStream.Position.Should().Be(bytes.Length);
        memoryStream.Length.Should().Be(stream.Length);
        memoryStream.ToArray().Should().BeEquivalentTo(bytes);

        // assert original stream is not changed
        stream.Position.Should().Be(bytes.Length);
        stream.Length.Should().Be(bytes.Length);
        stream.ToArray().Should().BeEquivalentTo(bytes);
    }

    #endregion
}
