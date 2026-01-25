// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.IO;

namespace Tests.IO;

public sealed class ActionableStreamTests
{
    [Fact]
    public void should_throw_when_stream_null()
    {
        // given
        Stream? stream = null;

        // when
        var act = () => new ActionableStream(stream!, () => { });

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_delegate_CanRead_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when/then
        sut.CanRead.Should().Be(inner.CanRead);
    }

    [Fact]
    public void should_delegate_CanWrite_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when/then
        sut.CanWrite.Should().Be(inner.CanWrite);
    }

    [Fact]
    public void should_delegate_CanSeek_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when/then
        sut.CanSeek.Should().Be(inner.CanSeek);
    }

    [Fact]
    public void should_delegate_Length_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3, 4, 5]);
        using var sut = new ActionableStream(inner, () => { });

        // when/then
        sut.Length.Should().Be(inner.Length);
    }

    [Fact]
    public void should_delegate_Position_get_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3, 4, 5]);
        inner.Position = 3;
        using var sut = new ActionableStream(inner, () => { });

        // when/then
        sut.Position.Should().Be(3);
    }

    [Fact]
    public void should_delegate_Position_set_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3, 4, 5]);
        using var sut = new ActionableStream(inner, () => { });

        // when
        sut.Position = 2;

        // then
        inner.Position.Should().Be(2);
    }

    [Fact]
    public void should_delegate_Read_to_inner_stream()
    {
        // given
        byte[] data = [10, 20, 30, 40, 50];
        using var inner = new MemoryStream(data);
        using var sut = new ActionableStream(inner, () => { });
        var buffer = new byte[3];

        // when
        var bytesRead = sut.Read(buffer, 0, 3);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public async Task should_delegate_ReadAsync_to_inner_stream()
    {
        // given
        byte[] data = [10, 20, 30, 40, 50];
        using var inner = new MemoryStream(data);
        using var sut = new ActionableStream(inner, () => { });
        var buffer = new byte[3];

        // when
        var bytesRead = await sut.ReadAsync(buffer.AsMemory(), TestContext.Current.CancellationToken);

        // then
        bytesRead.Should().Be(3);
        buffer.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public void should_delegate_Write_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });
        byte[] data = [1, 2, 3];

        // when
        sut.Write(data, 0, 3);

        // then
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task should_delegate_WriteAsync_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });
        byte[] data = [1, 2, 3];

        // when
        await sut.WriteAsync(data.AsMemory(), TestContext.Current.CancellationToken);

        // then
        inner.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public void should_delegate_Flush_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when/then - no exception means delegation works
        sut.Flush();
    }

    [Fact]
    public void should_delegate_Seek_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream([1, 2, 3, 4, 5]);
        using var sut = new ActionableStream(inner, () => { });

        // when
        var position = sut.Seek(2, SeekOrigin.Begin);

        // then
        position.Should().Be(2);
        inner.Position.Should().Be(2);
    }

    [Fact]
    public void should_invoke_disposeAction_on_Dispose()
    {
        // given
        var actionInvoked = false;
        var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => actionInvoked = true);

        // when - use reflection to call Dispose(bool) since class is sealed
        _InvokeDispose(sut, disposing: true);

        // then
        actionInvoked.Should().BeTrue();
    }

    [Fact]
    public void should_dispose_inner_stream_after_action()
    {
        // given
        var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when
        _InvokeDispose(sut, disposing: true);

        // then - inner stream should be disposed (accessing it throws)
        var act = () => inner.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void should_not_throw_if_disposeAction_throws()
    {
        // given
        var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => throw new InvalidOperationException("test"));

        // when
        var act = () => _InvokeDispose(sut, disposing: true);

        // then
        act.Should().NotThrow();
    }

    private static void _InvokeDispose(ActionableStream stream, bool disposing)
    {
        var method = typeof(ActionableStream).GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(bool)]
        );
        method!.Invoke(stream, [disposing]);
    }

    [Fact]
    public void should_delegate_CanTimeout_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when/then
        sut.CanTimeout.Should().Be(inner.CanTimeout);
    }

    [Fact]
    public void should_delegate_SetLength_to_inner_stream()
    {
        // given
        using var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => { });

        // when
        sut.SetLength(100);

        // then
        inner.Length.Should().Be(100);
    }
}
