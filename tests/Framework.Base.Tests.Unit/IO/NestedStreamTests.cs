using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Framework.Checks;
using Framework.Core;
using Framework.IO;
using Framework.Testing.Tests;

namespace Tests.IO;

#pragma warning disable FAA0002
public sealed class NestedStreamTests : TestBase
{
    private const int _DefaultNestedLength = 10;
    private readonly MemoryStream _underlyingStream;
    private Stream _stream;

    private readonly CancellationTokenSource _timeoutTokenSource = new(
        Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(10)
    );

    private CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : _timeoutTokenSource.Token;

    public NestedStreamTests(ITestOutputHelper logger)
        : base(logger)
    {
        var random = new Random();
        var buffer = new byte[20];
        random.NextBytes(buffer);
        _underlyingStream = new MemoryStream(buffer);
        _stream = _underlyingStream.ReadSlice(_DefaultNestedLength);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
            _underlyingStream.Dispose();
            _timeoutTokenSource.Dispose();
        }

        base.Dispose(disposing);
    }

    [Fact]
    public void Slice_InputValidation()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.ReadSlice(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryStream().ReadSlice(-1));

        var noReadStream = Substitute.For<Stream>();
        Assert.Throws<ArgumentException>(() => noReadStream.ReadSlice(1));

        // Assert that read functions were not called.
        Assert.Same(
            typeof(Stream)
                .GetProperty(
                    nameof(Stream.CanRead),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )!
                .GetMethod,
            Assert.Single(noReadStream.ReceivedCalls()).GetMethodInfo()
        );
    }

    [Fact]
    public void CanSeek()
    {
        Assert.True(_stream.CanSeek);
        _stream.Dispose();
        Assert.False(_stream.CanSeek);
    }

    [Fact]
    public void CanSeek_NonSeekableStream()
    {
        using var gzipStream = new GZipStream(Stream.Null, CompressionMode.Decompress);
        using var stream = gzipStream.ReadSlice(10);

        Assert.False(stream.CanSeek);
        // ReSharper disable once DisposeOnUsingVariable
        stream.Dispose();
        Assert.False(stream.CanSeek);
    }

    [Fact]
    public void Length()
    {
        Assert.Equal(_DefaultNestedLength, _stream.Length);
        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Length);
    }

    [Fact]
    public void Length_NonSeekableStream()
    {
        using var gzipStream = new GZipStream(Stream.Null, CompressionMode.Decompress);
        using var stream = gzipStream.ReadSlice(10);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        // ReSharper disable once DisposeOnUsingVariable
        stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stream.Length);
    }

    [Fact]
    public void Position()
    {
        var buffer = new byte[_DefaultNestedLength];

        Assert.Equal(0, _stream.Position);
        var bytesRead = _stream.Read(buffer, 0, 5);
        Assert.Equal(bytesRead, _stream.Position);

        _stream.Position = 0;
        var buffer2 = new byte[_DefaultNestedLength];
        bytesRead = _stream.Read(buffer2, 0, 5);
        Assert.Equal(bytesRead, _stream.Position);
        Assert.Equal(buffer, buffer2);

        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Position);
        Assert.Throws<ObjectDisposedException>(() => _stream.Position = 0);
    }

    [Fact]
    public void Position_NonSeekableStream()
    {
        using var nonSeekableWrapper = new NonSeekableStream(_underlyingStream);
        using var stream = nonSeekableWrapper.ReadSlice(10);

        Assert.Equal(0, stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 3);
        Assert.Equal(0, stream.Position);
        stream.ReadByte();
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public void IsDisposed()
    {
        Assert.False(((IHasIsDisposed)_stream).IsDisposed);
        _stream.Dispose();
        Assert.True(((IHasIsDisposed)_stream).IsDisposed);
    }

    [Fact]
    public void Dispose_DoesNotDisposeUnderlyingStream()
    {
        _stream.Dispose();
        Assert.True(_underlyingStream.CanSeek);
        // A sanity check that if it were disposed, our assertion above would fail.
        _underlyingStream.Dispose();
        Assert.False(_underlyingStream.CanSeek);
    }

    [Fact]
    public void SetLength()
    {
        Assert.Throws<NotSupportedException>(() => _stream.SetLength(0));
        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.SetLength(0));
    }

    [Fact]
    public void Seek_Current()
    {
        Assert.Equal(0, _stream.Position);
        Assert.Equal(0, _stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(0, _underlyingStream.Position);
        Assert.Throws<IOException>(() => _stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(0, _underlyingStream.Position);

        Assert.Equal(5, _stream.Seek(5, SeekOrigin.Current));
        Assert.Equal(5, _underlyingStream.Position);
        Assert.Equal(5, _stream.Seek(0, SeekOrigin.Current));
        Assert.Equal(5, _underlyingStream.Position);
        Assert.Equal(4, _stream.Seek(-1, SeekOrigin.Current));
        Assert.Equal(4, _underlyingStream.Position);
        Assert.Throws<IOException>(() => _stream.Seek(-10, SeekOrigin.Current));
        Assert.Equal(4, _underlyingStream.Position);

        Assert.Equal(0, _stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, _stream.Position);

        Assert.Equal(_DefaultNestedLength + 1, _stream.Seek(_DefaultNestedLength + 1, SeekOrigin.Current));
        Assert.Equal(_DefaultNestedLength + 1, _underlyingStream.Position);
        Assert.Equal((2 * _DefaultNestedLength) + 1, _stream.Seek(_DefaultNestedLength, SeekOrigin.Current));
        Assert.Equal((2 * _DefaultNestedLength) + 1, _underlyingStream.Position);
        Assert.Equal((2 * _DefaultNestedLength) + 1, _stream.Seek(0, SeekOrigin.Current));
        Assert.Equal((2 * _DefaultNestedLength) + 1, _underlyingStream.Position);
        Assert.Equal(1, _stream.Seek(-2 * _DefaultNestedLength, SeekOrigin.Current));
        Assert.Equal(1, _underlyingStream.Position);

        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Sook_WithNonStartPositionInUnderlyingStream()
    {
        _underlyingStream.Position = 1;
        _stream = _underlyingStream.ReadSlice(5);

        Assert.Equal(0, _stream.Position);
        Assert.Equal(2, _stream.Seek(2, SeekOrigin.Current));
        Assert.Equal(3, _underlyingStream.Position);
    }

    [Fact]
    public void Seek_Begin()
    {
        Assert.Equal(0, _stream.Position);
        Assert.Throws<IOException>(() => _stream.Seek(-1, SeekOrigin.Begin));
        Assert.Equal(0, _underlyingStream.Position);

        Assert.Equal(0, _stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(0, _underlyingStream.Position);

        Assert.Equal(5, _stream.Seek(5, SeekOrigin.Begin));
        Assert.Equal(5, _underlyingStream.Position);

        Assert.Equal(_DefaultNestedLength, _stream.Seek(_DefaultNestedLength, SeekOrigin.Begin));
        Assert.Equal(_DefaultNestedLength, _underlyingStream.Position);

        Assert.Equal(_DefaultNestedLength + 1, _stream.Seek(_DefaultNestedLength + 1, SeekOrigin.Begin));
        Assert.Equal(_DefaultNestedLength + 1, _underlyingStream.Position);

        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public void Seek_End()
    {
        Assert.Equal(0, _stream.Position);
        Assert.Equal(9, _stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(9, _underlyingStream.Position);

        Assert.Equal(_DefaultNestedLength, _stream.Seek(0, SeekOrigin.End));
        Assert.Equal(_DefaultNestedLength, _underlyingStream.Position);

        Assert.Equal(_DefaultNestedLength + 5, _stream.Seek(5, SeekOrigin.End));
        Assert.Equal(_DefaultNestedLength + 5, _underlyingStream.Position);

        Assert.Throws<IOException>(() => _stream.Seek(-20, SeekOrigin.Begin));
        Assert.Equal(_DefaultNestedLength + 5, _underlyingStream.Position);

        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Seek(0, SeekOrigin.End));
    }

    [Fact]
    public void Flush()
    {
        Assert.Throws<NotSupportedException>(() => _stream.Flush());
        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Flush());
    }

    [Fact]
    public async Task FlushAsync()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _stream.FlushAsync());
        await _stream.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _stream.FlushAsync());
    }

    [Fact]
    public void CanRead()
    {
        Assert.True(_stream.CanRead);
        _stream.Dispose();
        Assert.False(_stream.CanRead);
    }

    [Fact]
    public void CanWrite()
    {
        Assert.False(_stream.CanWrite);
        _stream.Dispose();
        Assert.False(_stream.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_Throws()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _stream.WriteAsync(new byte[1], 0, 1).WithCancellation(TimeoutToken)
        );

        await _stream.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _stream.WriteAsync(new byte[1], 0, 1).WithCancellation(TimeoutToken)
        );
    }

    [Fact]
    public void Write_Throws()
    {
        Assert.Throws<NotSupportedException>(() => _stream.Write(new byte[1], 0, 1));
        _stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _stream.Write(new byte[1], 0, 1));
    }

    [Fact]
    public async Task ReadAsync_Empty_ReturnsZero()
    {
        Assert.Equal(0, await _stream.ReadAsync([], 0, 0, CancellationToken.None).WithCancellation(TimeoutToken));
    }

    [Fact]
    public async Task Read_BeyondEndOfStream_ReturnsZero()
    {
        // Seek beyond the end of the stream
        _stream.Seek(1, SeekOrigin.End);

        var buffer = new byte[_underlyingStream.Length];

        Assert.Equal(0, await _stream.ReadAsync(buffer, 0, buffer.Length, TimeoutToken).WithCancellation(TimeoutToken));
    }

    [Fact]
    public async Task ReadAsync_NoMoreThanGiven()
    {
        var buffer = new byte[_underlyingStream.Length];

        int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, TimeoutToken).WithCancellation(TimeoutToken);

        Assert.Equal(_DefaultNestedLength, bytesRead);

        Assert.Equal(
            0,
            await _stream
                .ReadAsync(buffer, bytesRead, buffer.Length - bytesRead, TimeoutToken)
                .WithCancellation(TimeoutToken)
        );

        Assert.Equal(_DefaultNestedLength, _underlyingStream.Position);
    }

    [Fact]
    public void Read_NoMoreThanGiven()
    {
        var buffer = new byte[_underlyingStream.Length];
        var bytesRead = _stream.Read(buffer, 0, buffer.Length);
        Assert.Equal(_DefaultNestedLength, bytesRead);

        Assert.Equal(0, _stream.Read(buffer, bytesRead, buffer.Length - bytesRead));
        Assert.Equal(_DefaultNestedLength, _underlyingStream.Position);
    }

    [Fact]
    public void Read_Empty_ReturnsZero()
    {
        Assert.Equal(0, _stream.Read([], 0, 0));
    }

    [Fact]
    public async Task ReadAsync_WhenLengthIsInitially0()
    {
        _stream = _underlyingStream.ReadSlice(0);

        Assert.Equal(0, await _stream.ReadAsync(new byte[1], 0, 1, TimeoutToken).WithCancellation(TimeoutToken));
    }

    [Fact]
    public void Read_WhenLengthIsInitially0()
    {
        _stream = _underlyingStream.ReadSlice(0);
        Assert.Equal(0, _stream.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void CreationDoesNotReadFromUnderlyingStream()
    {
        Assert.Equal(0, _underlyingStream.Position);
    }

    [Fact]
    public void Read_UnderlyingStreamReturnsFewerBytesThanRequested()
    {
        var buffer = new byte[20];
        var firstBlockLength = _DefaultNestedLength / 2;
        _underlyingStream.SetLength(firstBlockLength);
        Assert.Equal(firstBlockLength, _stream.Read(buffer, 0, buffer.Length));
        _underlyingStream.SetLength(_DefaultNestedLength * 2);
        Assert.Equal(_DefaultNestedLength - firstBlockLength, _stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task ReadAsync_UnderlyingStreamReturnsFewerBytesThanRequested()
    {
        var buffer = new byte[20];
        const int firstBlockLength = _DefaultNestedLength / 2;
        _underlyingStream.SetLength(firstBlockLength);
        Assert.Equal(firstBlockLength, await _stream.ReadAsync(buffer));
        _underlyingStream.SetLength(_DefaultNestedLength * 2);
        Assert.Equal(_DefaultNestedLength - firstBlockLength, await _stream.ReadAsync(buffer));
    }

    [Fact]
    public void Read_ValidatesArguments()
    {
        var buffer = new byte[20];

        Assert.Throws<ArgumentNullException>(() => _stream.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _stream.Read(buffer, -1, buffer.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => _stream.Read(buffer, 0, -1));
        Assert.Throws<ArgumentException>(() => _stream.Read(buffer, 1, buffer.Length));
    }

    [Fact]
    public async Task ReadAsync_ValidatesArguments()
    {
        var buffer = new byte[20];

        await Assert.ThrowsAsync<ArgumentNullException>(() => _stream.ReadAsync(null!, 0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _stream.ReadAsync(buffer, -1, buffer.Length));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _stream.ReadAsync(buffer, 0, -1));
        await Assert.ThrowsAsync<ArgumentException>(() => _stream.ReadAsync(buffer, 1, buffer.Length));
    }

    [Fact]
    public void Read_ThrowsIfDisposed()
    {
        _stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _stream.Read([], 0, 0));
    }

    [Fact]
    public async Task ReadAsync_ThrowsIfDisposed()
    {
        await _stream.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => _stream.ReadAsync([], 0, 0));
    }
}
