// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Checks;
using Microsoft;

namespace Framework.IO;

/// <summary>Exposes <see cref="ReadOnlySequence{T}"/> as a <see cref="Stream"/></summary>
/// <remarks>
/// Copy of https://github.com/dotnet/Nerdbank.Streams/blob/main/src/Nerdbank.Streams/ReadOnlySequenceStream.cs
/// </remarks>
internal class ReadOnlySequenceStream : Stream
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

    /// <inheritdoc/>
    public override bool CanRead => !IsDisposed;

    /// <inheritdoc/>
    public override bool CanSeek => !IsDisposed;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _ReturnOrThrowDisposed(_readOnlySequence.Length);

    /// <inheritdoc/>
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

    public override void Flush() => _ThrowDisposedOr(new NotSupportedException());

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        throw _ThrowDisposedOr(new NotSupportedException());

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _readOnlySequence.Slice(_position);
        var toCopy = remaining.Slice(0, Math.Min(count, remaining.Length));
        _position = toCopy.End;
        toCopy.CopyTo(buffer.AsSpan(offset, count));

        return (int)toCopy.Length;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytesRead = Read(buffer, offset, count);

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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override void SetLength(long value) => _ThrowDisposedOr(new NotSupportedException());

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => _ThrowDisposedOr(new NotSupportedException());

    /// <inheritdoc/>
    public override void WriteByte(byte value) => _ThrowDisposedOr(new NotSupportedException());

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        throw _ThrowDisposedOr(new NotSupportedException());

    /// <inheritdoc/>
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        foreach (var segment in _readOnlySequence)
        {
            await destination.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        var remaining = _readOnlySequence.Slice(_position);
        var toCopy = remaining.Slice(0, Math.Min(buffer.Length, remaining.Length));
        _position = toCopy.End;
        toCopy.CopyTo(buffer);

        return (int)toCopy.Length;
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<int>(Read(buffer.Span));
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => throw _ThrowDisposedOr(new NotSupportedException());

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        throw _ThrowDisposedOr(new NotSupportedException());

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            _disposeAction?.Invoke(_disposeActionArg);
            base.Dispose(disposing);
        }
    }

    private T _ReturnOrThrowDisposed<T>(T value)
    {
        Ensure.NotDisposed(IsDisposed, this);

        return value;
    }

    private Exception _ThrowDisposedOr(Exception e)
    {
        Ensure.NotDisposed(IsDisposed, this);

        throw e;
    }
}
