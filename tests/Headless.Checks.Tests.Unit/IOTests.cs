// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IoTests
{
    [Fact]
    public void can_read_and_write_and_seek()
    {
        // given
        using var stream = new MemoryStream();

        // then
        Argument.CanRead(stream);
        Argument.CanWrite(stream);
        Argument.CanSeek(stream);
    }

    [Fact]
    public void is_at_start_position_should_throw_for_stream_not_at_start_position()
    {
        // given
        using var stream = new MemoryStream();
        stream.Write([0x01]);
        stream.Position = 1;

        // when
        // ReSharper disable once AccessToDisposedClosure
        var action = () => Argument.IsAtStartPosition(stream);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The stream argument \"stream\" of type <MemoryStream must be at the starting position. (Actual Position 1) (Parameter 'stream')"
            );
    }

    [Fact]
    public void can_read_should_throw_when_stream_cannot_be_read()
    {
        // given
        using var stream = new NonReadableStream();

        // when
        var action = () => Argument.CanRead(stream);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void can_write_should_throw_when_stream_cannot_be_written()
    {
        // given
        using var stream = new MemoryStream([], writable: false);

        // when
        var action = () => Argument.CanWrite(stream);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void can_seek_should_throw_when_stream_cannot_seek()
    {
        // given
        using var stream = new NonSeekableStream();

        // when
        var action = () => Argument.CanSeek(stream);

        // then
        action.Should().ThrowExactly<ArgumentException>();
    }

    // FileExists tests

    [Fact]
    public void file_exists_should_return_path_when_file_exists()
    {
        // given - use a file that always exists
        var path = typeof(IoTests).Assembly.Location;

        // when
        var result = Argument.FileExists(path);

        // then
        result.Should().Be(path);
    }

    [Fact]
    public void file_exists_should_throw_when_file_does_not_exist()
    {
        // given
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent.txt");

        // when
        var action = () => Argument.FileExists(path);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"The file \"path\" at path \"{path}\" does not exist. (Parameter 'path')");
    }

    [Fact]
    public void file_exists_should_throw_for_null_path()
    {
        // given
        string? path = null;

        // when
        var action = () => Argument.FileExists(path);

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("path");
    }

    [Fact]
    public void file_exists_should_throw_for_empty_path()
    {
        // given
        var path = "";

        // when
        var action = () => Argument.FileExists(path);

        // then
        action.Should().ThrowExactly<ArgumentException>().WithParameterName("path");
    }

    [Fact]
    public void file_exists_should_use_custom_message()
    {
        // given
        var path = "/nonexistent/file.txt";
        const string customMessage = "Custom file not found message";

        // when
        var action = () => Argument.FileExists(path, customMessage);

        // then
        action.Should().ThrowExactly<ArgumentException>().WithMessage($"{customMessage} (Parameter 'path')");
    }

    // DirectoryExists tests

    [Fact]
    public void directory_exists_should_return_path_when_directory_exists()
    {
        // given - use temp directory that always exists
        var path = Path.GetTempPath();

        // when
        var result = Argument.DirectoryExists(path);

        // then
        result.Should().Be(path);
    }

    [Fact]
    public void directory_exists_should_throw_when_directory_does_not_exist()
    {
        // given
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent");

        // when
        var action = () => Argument.DirectoryExists(path);

        // then
        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage($"The directory \"path\" at path \"{path}\" does not exist. (Parameter 'path')");
    }

    [Fact]
    public void directory_exists_should_throw_for_null_path()
    {
        // given
        string? path = null;

        // when
        var action = () => Argument.DirectoryExists(path);

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("path");
    }

    [Fact]
    public void directory_exists_should_throw_for_empty_path()
    {
        // given
        var path = "";

        // when
        var action = () => Argument.DirectoryExists(path);

        // then
        action.Should().ThrowExactly<ArgumentException>().WithParameterName("path");
    }

    [Fact]
    public void directory_exists_should_use_custom_message()
    {
        // given
        var path = "/nonexistent/directory";
        const string customMessage = "Custom directory not found message";

        // when
        var action = () => Argument.DirectoryExists(path, customMessage);

        // then
        action.Should().ThrowExactly<ArgumentException>().WithMessage($"{customMessage} (Parameter 'path')");
    }

    // Helper classes for testing

    private sealed class NonReadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
