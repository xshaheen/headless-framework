// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

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
}
