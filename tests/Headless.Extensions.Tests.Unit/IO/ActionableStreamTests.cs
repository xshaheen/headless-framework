// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.IO;

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
    public void should_throw_when_disposeAction_null()
    {
        // given
        Action? disposeAction = null;

        // when - a null action must fail fast at construction, not be swallowed inside the dispose catch.
        var act = () => new ActionableStream(new MemoryStream(), disposeAction!);

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
        await using var inner = new MemoryStream(data);
        await using var sut = new ActionableStream(inner, () => { });
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
        await using var inner = new MemoryStream();
        await using var sut = new ActionableStream(inner, () => { });
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
    public void should_invoke_disposeAction_exactly_once_on_public_Dispose()
    {
        // given
        var invocationCount = 0;
        var inner = new MemoryStream();
        var sut = new ActionableStream(inner, () => invocationCount++);

        // when - dispose multiple times via the public surface
        sut.Dispose();
        sut.Dispose();
        sut.Dispose();

        // then - the action fired exactly once (idempotent dispose)
        invocationCount.Should().Be(1);
    }

    [Fact]
    public void should_invoke_disposeAction_when_closed()
    {
        // given - Close routes through Dispose so the action must still fire
        var invocationCount = 0;
        var inner = new MemoryStream();
        var sut = new ActionableStream(inner, () => invocationCount++);

        // when
        sut.Close();

        // then
        invocationCount.Should().Be(1);
    }

    [Fact]
    public void should_not_invoke_disposeAction_on_finalizer_path()
    {
        // given
        var invocationCount = 0;
        var inner = new MemoryStream();
        using var sut = new ActionableStream(inner, () => invocationCount++);

        // when - simulate the finalizer path (disposing: false)
        _InvokeDispose(sut, disposing: false);

        // then - the action must not run while the GC is reclaiming managed resources
        invocationCount.Should().Be(0);

        // inner stream remains usable because finalizer path did not dispose it
        var act = () => inner.ReadByte();
        act.Should().NotThrow();
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

    [Fact]
    public async Task should_invoke_disposeAction_on_DisposeAsync()
    {
        // given
        var actionInvoked = false;
        var inner = new MemoryStream();
        var sut = new ActionableStream(inner, () => actionInvoked = true);

        // when
        await sut.DisposeAsync();

        // then
        actionInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task should_dispose_inner_stream_on_DisposeAsync()
    {
        // given
        var inner = new MemoryStream();
        var sut = new ActionableStream(inner, () => { });

        // when
        await sut.DisposeAsync();

        // then - inner stream should be disposed (accessing it throws)
        var act = () => inner.ReadByte();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_invoke_disposeAction_exactly_once_across_DisposeAsync_and_Dispose()
    {
        // given
        var invocationCount = 0;
        var inner = new MemoryStream();
        var sut = new ActionableStream(inner, () => invocationCount++);

        // when - dispose via both the async and sync surfaces
        await sut.DisposeAsync();
        await sut.DisposeAsync();
#pragma warning disable VSTHRD103 // Deliberately exercising the synchronous Dispose surface for idempotency.
        sut.Dispose();
#pragma warning restore VSTHRD103

        // then - the action fired exactly once (idempotent disposal)
        invocationCount.Should().Be(1);
    }

    [Fact]
    public async Task should_not_throw_if_disposeAction_throws_on_DisposeAsync()
    {
        // given
        var inner = new MemoryStream();
        await using var sut = new ActionableStream(inner, () => throw new InvalidOperationException("test"));

        // when
        var act = async () => await sut.DisposeAsync();

        // then - the inner stream is still disposed despite the action throwing
        await act.Should().NotThrowAsync();
        var read = () => inner.ReadByte();
        read.Should().Throw<ObjectDisposedException>();
    }
}
