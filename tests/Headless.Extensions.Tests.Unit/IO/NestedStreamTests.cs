using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Headless.Core;
using Headless.IO;
using Headless.Testing.Tests;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests.IO;

public sealed class NestedStreamTests : TestBase
{
    private const int _DefaultNestedLength = 10;
    private readonly MemoryStream _underlyingStream;
    private Stream _stream;

    private readonly CancellationTokenSource _timeoutTokenSource = new(
        Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10)
    );

    private CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : _timeoutTokenSource.Token;

    public NestedStreamTests()
    {
        var random = new Random();
        var buffer = new byte[20];
        random.NextBytes(buffer);
        _underlyingStream = new MemoryStream(buffer);
        _stream = _underlyingStream.ReadSlice(_DefaultNestedLength);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _timeoutTokenSource.Dispose();
        await _stream.DisposeAsync();
        await _underlyingStream.DisposeAsync();

        await base.DisposeAsyncCore();
    }

    [Fact]
    public void slice_input_validation()
    {
        var actNull = () => StreamExtensions.ReadSlice(null!, 1);
        actNull.Should().Throw<ArgumentNullException>();

        var actNegative = () => new MemoryStream().ReadSlice(-1);
        actNegative.Should().Throw<ArgumentOutOfRangeException>();

        var noReadStream = Substitute.For<Stream>();
        var actNoRead = () => noReadStream.ReadSlice(1);
        actNoRead.Should().Throw<ArgumentException>();

        // then that read functions were not called.
        noReadStream
            .ReceivedCalls()
            .Should()
            .ContainSingle()
            .Which.GetMethodInfo()
            .Should()
            .BeSameAs(
                typeof(Stream)
                    .GetProperty(
                        nameof(Stream.CanRead),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )!
                    .GetMethod
            );
    }

    [Fact]
    public void can_seek()
    {
        _stream.CanSeek.Should().BeTrue();
        _stream.Dispose();
        _stream.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void can_seek_non_seekable_stream()
    {
        using var gzipStream = new GZipStream(Stream.Null, CompressionMode.Decompress);
        using var stream = gzipStream.ReadSlice(10);

        stream.CanSeek.Should().BeFalse();
        // ReSharper disable once DisposeOnUsingVariable
        stream.Dispose();
        stream.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void length()
    {
        _stream.Length.Should().Be(_DefaultNestedLength);
        _stream.Dispose();
        var act = () => _ = _stream.Length;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void length_non_seekable_stream()
    {
        using var gzipStream = new GZipStream(Stream.Null, CompressionMode.Decompress);
        using var stream = gzipStream.ReadSlice(10);
        var act = () => _ = stream.Length;
        act.Should().Throw<NotSupportedException>();
        // ReSharper disable once DisposeOnUsingVariable
        stream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void position()
    {
        var buffer = new byte[_DefaultNestedLength];

        _stream.Position.Should().Be(0);
        var bytesRead = _stream.Read(buffer, 0, 5);
        _stream.Position.Should().Be(bytesRead);

        _stream.Position = 0;
        var buffer2 = new byte[_DefaultNestedLength];
        bytesRead = _stream.Read(buffer2, 0, 5);
        _stream.Position.Should().Be(bytesRead);
        buffer2.Should().Equal(buffer);

        _stream.Dispose();
        var actGet = () => _ = _stream.Position;
        actGet.Should().Throw<ObjectDisposedException>();
        var actSet = () => _stream.Position = 0;
        actSet.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void position_non_seekable_stream()
    {
        using var nonSeekableWrapper = new NonSeekableStream(_underlyingStream);
        using var stream = nonSeekableWrapper.ReadSlice(10);

        stream.Position.Should().Be(0);
        var act = () => stream.Position = 3;
        act.Should().Throw<NotSupportedException>();
        stream.Position.Should().Be(0);
        stream.ReadByte();
        stream.Position.Should().Be(1);
    }

    [Fact]
    public void is_disposed()
    {
        ((IHasIsDisposed)_stream).IsDisposed.Should().BeFalse();
        _stream.Dispose();
        ((IHasIsDisposed)_stream).IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void dispose_does_not_dispose_underlying_stream()
    {
        _stream.Dispose();
        _underlyingStream.CanSeek.Should().BeTrue();
        // A sanity check that if it were disposed, our assertion above would fail.
        _underlyingStream.Dispose();
        _underlyingStream.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void set_length()
    {
        var act = () => _stream.SetLength(0);
        act.Should().Throw<NotSupportedException>();
        _stream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void seek_current()
    {
        _stream.Position.Should().Be(0);
        _stream.Seek(0, SeekOrigin.Current).Should().Be(0);
        _underlyingStream.Position.Should().Be(0);
        var actBackBeforeStart = () => _stream.Seek(-1, SeekOrigin.Current);
        actBackBeforeStart.Should().Throw<IOException>();
        _underlyingStream.Position.Should().Be(0);

        _stream.Seek(5, SeekOrigin.Current).Should().Be(5);
        _underlyingStream.Position.Should().Be(5);
        _stream.Seek(0, SeekOrigin.Current).Should().Be(5);
        _underlyingStream.Position.Should().Be(5);
        _stream.Seek(-1, SeekOrigin.Current).Should().Be(4);
        _underlyingStream.Position.Should().Be(4);
        var actUnderflow = () => _stream.Seek(-10, SeekOrigin.Current);
        actUnderflow.Should().Throw<IOException>();
        _underlyingStream.Position.Should().Be(4);

        _stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        _stream.Position.Should().Be(0);

        _stream.Seek(_DefaultNestedLength + 1, SeekOrigin.Current).Should().Be(_DefaultNestedLength + 1);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength + 1);
        _stream.Seek(_DefaultNestedLength, SeekOrigin.Current).Should().Be((2 * _DefaultNestedLength) + 1);
        _underlyingStream.Position.Should().Be((2 * _DefaultNestedLength) + 1);
        _stream.Seek(0, SeekOrigin.Current).Should().Be((2 * _DefaultNestedLength) + 1);
        _underlyingStream.Position.Should().Be((2 * _DefaultNestedLength) + 1);
        _stream.Seek(-2 * _DefaultNestedLength, SeekOrigin.Current).Should().Be(1);
        _underlyingStream.Position.Should().Be(1);

        _stream.Dispose();
        var actDisposed = () => _stream.Seek(0, SeekOrigin.Begin);
        actDisposed.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void sook_with_non_start_position_in_underlying_stream()
    {
        _underlyingStream.Position = 1;
        _stream = _underlyingStream.ReadSlice(5);

        _stream.Position.Should().Be(0);
        _stream.Seek(2, SeekOrigin.Current).Should().Be(2);
        _underlyingStream.Position.Should().Be(3);
    }

    [Fact]
    public void seek_begin()
    {
        _stream.Position.Should().Be(0);
        var actBeforeStart = () => _stream.Seek(-1, SeekOrigin.Begin);
        actBeforeStart.Should().Throw<IOException>();
        _underlyingStream.Position.Should().Be(0);

        _stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        _underlyingStream.Position.Should().Be(0);

        _stream.Seek(5, SeekOrigin.Begin).Should().Be(5);
        _underlyingStream.Position.Should().Be(5);

        _stream.Seek(_DefaultNestedLength, SeekOrigin.Begin).Should().Be(_DefaultNestedLength);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength);

        _stream.Seek(_DefaultNestedLength + 1, SeekOrigin.Begin).Should().Be(_DefaultNestedLength + 1);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength + 1);

        _stream.Dispose();
        var actDisposed = () => _stream.Seek(0, SeekOrigin.Begin);
        actDisposed.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void seek_end()
    {
        _stream.Position.Should().Be(0);
        _stream.Seek(-1, SeekOrigin.End).Should().Be(9);
        _underlyingStream.Position.Should().Be(9);

        _stream.Seek(0, SeekOrigin.End).Should().Be(_DefaultNestedLength);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength);

        _stream.Seek(5, SeekOrigin.End).Should().Be(_DefaultNestedLength + 5);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength + 5);

        var actBeforeStart = () => _stream.Seek(-20, SeekOrigin.Begin);
        actBeforeStart.Should().Throw<IOException>();
        _underlyingStream.Position.Should().Be(_DefaultNestedLength + 5);

        _stream.Dispose();
        var actDisposed = () => _stream.Seek(0, SeekOrigin.End);
        actDisposed.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void flush()
    {
        var act = () => _stream.Flush();
        act.Should().Throw<NotSupportedException>();
        _stream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task flush_async()
    {
        var act = () => _stream.FlushAsync(AbortToken);
        await act.Should().ThrowAsync<NotSupportedException>();
        await _stream.DisposeAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void can_read()
    {
        _stream.CanRead.Should().BeTrue();
        _stream.Dispose();
        _stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void can_write()
    {
        _stream.CanWrite.Should().BeFalse();
        _stream.Dispose();
        _stream.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task write_async_throws()
    {
        var act = () => _stream.WriteAsync(new byte[1], 0, 1, AbortToken).WithCancellation(TimeoutToken);
        await act.Should().ThrowAsync<NotSupportedException>();

        await _stream.DisposeAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void write_throws()
    {
        var act = () => _stream.Write(new byte[1], 0, 1);
        act.Should().Throw<NotSupportedException>();
        _stream.Dispose();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task read_async_empty_returns_zero()
    {
        (await _stream.ReadAsync([], 0, 0, CancellationToken.None).WithCancellation(TimeoutToken)).Should().Be(0);
    }

    [Fact]
    public async Task read_beyond_end_of_stream_returns_zero()
    {
        // Seek beyond the end of the stream
        _stream.Seek(1, SeekOrigin.End);

        var buffer = new byte[_underlyingStream.Length];

        (await _stream.ReadAsync(buffer, 0, buffer.Length, TimeoutToken).WithCancellation(TimeoutToken)).Should().Be(0);
    }

    [Fact]
    public async Task read_async_no_more_than_given()
    {
        var buffer = new byte[_underlyingStream.Length];

        var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, TimeoutToken).WithCancellation(TimeoutToken);

        bytesRead.Should().Be(_DefaultNestedLength);

        (
            await _stream
                .ReadAsync(buffer, bytesRead, buffer.Length - bytesRead, TimeoutToken)
                .WithCancellation(TimeoutToken)
        )
            .Should()
            .Be(0);

        _underlyingStream.Position.Should().Be(_DefaultNestedLength);
    }

    [Fact]
    public void read_no_more_than_given()
    {
        var buffer = new byte[_underlyingStream.Length];
        var bytesRead = _stream.Read(buffer, 0, buffer.Length);
        bytesRead.Should().Be(_DefaultNestedLength);

        _stream.Read(buffer, bytesRead, buffer.Length - bytesRead).Should().Be(0);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength);
    }

    [Fact]
    public void read_span_no_more_than_given()
    {
        var buffer = new byte[_underlyingStream.Length];

        // The span overload clamps to the nested length even when the destination is larger.
        var bytesRead = _stream.Read(buffer.AsSpan());
        bytesRead.Should().Be(_DefaultNestedLength);

        _stream.Read(buffer.AsSpan(bytesRead)).Should().Be(0);
        _underlyingStream.Position.Should().Be(_DefaultNestedLength);
    }

    [Fact]
    public void read_span_beyond_end_of_stream_returns_zero()
    {
        // Seek beyond the end of the stream
        _stream.Seek(1, SeekOrigin.End);

        var buffer = new byte[_underlyingStream.Length];

        _stream.Read(buffer.AsSpan()).Should().Be(0);
    }

    [Fact]
    public void read_empty_returns_zero()
    {
        _stream.Read([], 0, 0).Should().Be(0);
    }

    [Fact]
    public async Task read_async_when_length_is_initially0()
    {
        _stream = _underlyingStream.ReadSlice(0);

        (await _stream.ReadAsync(new byte[1], 0, 1, TimeoutToken).WithCancellation(TimeoutToken)).Should().Be(0);
    }

    [Fact]
    public void read_when_length_is_initially0()
    {
        _stream = _underlyingStream.ReadSlice(0);
        _stream.Read(new byte[1], 0, 1).Should().Be(0);
    }

    [Fact]
    public void creation_does_not_read_from_underlying_stream()
    {
        _underlyingStream.Position.Should().Be(0);
    }

    [Fact]
    public void read_underlying_stream_returns_fewer_bytes_than_requested()
    {
        var buffer = new byte[20];
        const int firstBlockLength = _DefaultNestedLength / 2;
        _underlyingStream.SetLength(firstBlockLength);
        _stream.Read(buffer, 0, buffer.Length).Should().Be(firstBlockLength);
        _underlyingStream.SetLength(_DefaultNestedLength * 2);
        _stream.Read(buffer, 0, buffer.Length).Should().Be(_DefaultNestedLength - firstBlockLength);
    }

    [Fact]
    public async Task read_async_underlying_stream_returns_fewer_bytes_than_requested()
    {
        var buffer = new byte[20];
        const int firstBlockLength = _DefaultNestedLength / 2;
        _underlyingStream.SetLength(firstBlockLength);
        (await _stream.ReadAsync(buffer, AbortToken)).Should().Be(firstBlockLength);
        _underlyingStream.SetLength(_DefaultNestedLength * 2);
        (await _stream.ReadAsync(buffer, AbortToken)).Should().Be(_DefaultNestedLength - firstBlockLength);
    }

    [Fact]
    public void read_validates_arguments()
    {
        var buffer = new byte[20];

        var actNull = () => _stream.Read(null!, 0, 0);
        actNull.Should().Throw<ArgumentNullException>();
        var actNegativeOffset = () => _stream.Read(buffer, -1, buffer.Length);
        actNegativeOffset.Should().Throw<ArgumentOutOfRangeException>();
        var actNegativeCount = () => _stream.Read(buffer, 0, -1);
        actNegativeCount.Should().Throw<ArgumentOutOfRangeException>();
        var actTooLong = () => _stream.Read(buffer, 1, buffer.Length);
        actTooLong.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task read_async_validates_arguments()
    {
        var buffer = new byte[20];

        var actNull = () => _stream.ReadAsync(null!, 0, 0, AbortToken);
        await actNull.Should().ThrowAsync<ArgumentNullException>();
        var actNegativeOffset = () => _stream.ReadAsync(buffer, -1, buffer.Length, AbortToken);
        await actNegativeOffset.Should().ThrowAsync<ArgumentOutOfRangeException>();
        var actNegativeCount = () => _stream.ReadAsync(buffer, 0, -1, AbortToken);
        await actNegativeCount.Should().ThrowAsync<ArgumentOutOfRangeException>();
        var actTooLong = () => _stream.ReadAsync(buffer, 1, buffer.Length, AbortToken);
        await actTooLong.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void read_throws_if_disposed()
    {
        _stream.Dispose();

        var act = () => _stream.Read([], 0, 0);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task read_async_throws_if_disposed()
    {
        await _stream.DisposeAsync();

        var act = () => _stream.ReadAsync([], 0, 0, AbortToken);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
