// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.IO;

/// <summary>Exposes <see cref="ReadOnlySequence{T}"/> as a <see cref="Stream"/></summary>
/// <remarks>
/// Copy of <a href="https://github.com/dotnet/Nerdbank.Streams/blob/main/src/Nerdbank.Streams/ReadOnlySequenceStream.cs"></a>
/// </remarks>
internal sealed class ReadOnlySequenceStream : Stream
{
    private static readonly Task<int> _TaskOfZero = Task.FromResult(0);
    private readonly Action<object?>? _disposeAction;
    private readonly object? _disposeActionArg;
    private readonly ReadOnlySequence<byte> _readOnlySequence;
    private SequencePosition _position;

    /// <summary>A reusable task if two consecutive reads return the same number of bytes.</summary>
    private Task<int>? _lastReadTask;

    internal ReadOnlySequenceStream(
        ReadOnlySequence<byte> readOnlySequence,
        Action<object?>? disposeAction,
        object? disposeActionArg
    )
    {
        _readOnlySequence = readOnlySequence;
        _disposeAction = disposeAction;
        _disposeActionArg = disposeActionArg;
        _position = readOnlySequence.Start;
    }

    public override bool CanRead => !IsDisposed;

    public override bool CanSeek => !IsDisposed;

    public override bool CanWrite => false;

    public override long Length => _ReturnOrThrowDisposed(_readOnlySequence.Length);

    public override long Position
    {
        get => _readOnlySequence.Slice(0, _position).Length;
        set
        {
            Argument.IsPositiveOrZero(value, "Position must be >= 0");
            _position = _readOnlySequence.GetPosition(value, _readOnlySequence.Start);
        }
    }

    public bool IsDisposed { get; private set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _readOnlySequence.Slice(_position);
        var toCopy = remaining.Slice(0, Math.Min(count, remaining.Length));
        _position = toCopy.End;
        toCopy.CopyTo(buffer.AsSpan(offset, count));

        return (int)toCopy.Length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable MA0042 // Do not use blocking calls in an async method
        var bytesRead = Read(buffer, offset, count);
#pragma warning restore MA0042 // Do not use blocking calls in an async method

        if (bytesRead == 0)
        {
            return _TaskOfZero;
        }

#pragma warning disable VSTHRD103, CA1849, MA0042 // Call async methods when in an async method - This task is guaranteed to already be complete.
        if (_lastReadTask?.Result == bytesRead)
#pragma warning restore VSTHRD103, MA0042, CA1849 // Call async methods when in an async method
        {
            return _lastReadTask;
        }

        return _lastReadTask = Task.FromResult(bytesRead);
    }

    public override int ReadByte()
    {
        var remaining = _readOnlySequence.Slice(_position);

        if (remaining.Length > 0)
        {
            var result = remaining.First.Span[0];
            _position = _readOnlySequence.GetPosition(1, _position);

            return result;
        }

        return -1;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Ensure.NotDisposed(IsDisposed, this);
        SequencePosition relativeTo;

        switch (origin)
        {
            case SeekOrigin.Begin:
                relativeTo = _readOnlySequence.Start;

                break;
            case SeekOrigin.Current:
                if (offset >= 0)
                {
                    relativeTo = _position;
                }
                else
                {
                    relativeTo = _readOnlySequence.Start;
                    offset += Position;
                }

                break;
            case SeekOrigin.End:
                if (offset >= 0)
                {
                    relativeTo = _readOnlySequence.End;
                }
                else
                {
                    relativeTo = _readOnlySequence.Start;
                    offset += Length;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        _position = _readOnlySequence.GetPosition(offset, relativeTo);

        return Position;
    }

    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        foreach (var segment in _readOnlySequence)
        {
            await destination.WriteAsync(segment, cancellationToken).AnyContext();
        }
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _readOnlySequence.Slice(_position);
        var toCopy = remaining.Slice(0, Math.Min(buffer.Length, remaining.Length));
        _position = toCopy.End;
        toCopy.CopyTo(buffer);

        return (int)toCopy.Length;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<int>(Read(buffer.Span));
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            _disposeAction?.Invoke(_disposeActionArg);
            base.Dispose(disposing);
        }
    }

    #region Not Supported

    public override void SetLength(long value) => _ThrowDisposedOrNotSupported();

    public override void Write(byte[] buffer, int offset, int count)
    {
        _ThrowDisposedOrNotSupported();
    }

    public override void WriteByte(byte value)
    {
        _ThrowDisposedOrNotSupported();
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _ThrowDisposedOrNotSupported();

        return Task.CompletedTask;
    }

    public override void Write(ReadOnlySpan<byte> buffer) => _ThrowDisposedOrNotSupported();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _ThrowDisposedOrNotSupported();

        return ValueTask.CompletedTask;
    }

    public override void Flush() => _ThrowDisposedOrNotSupported();

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        _ThrowDisposedOrNotSupported();

        return Task.CompletedTask;
    }

    private T _ReturnOrThrowDisposed<T>(T value)
    {
        Ensure.NotDisposed(IsDisposed, this);

        return value;
    }

    [DoesNotReturn]
    private void _ThrowDisposedOrNotSupported()
    {
        Ensure.NotDisposed(IsDisposed, this);

        throw new NotSupportedException();
    }

    #endregion
}
