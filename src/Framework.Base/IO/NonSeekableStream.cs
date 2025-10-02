// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Core;

namespace Framework.IO;

#pragma warning disable CA1065 // Do not raise exceptions in property getters
public sealed class NonSeekableStream(Stream stream) : Stream, IHasIsDisposed
{
    public bool IsDisposed { get; private set; }

    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => stream.CanWrite;

    public override long Length => throw _ThrowDisposedOrNotSupported();

    public override long Position
    {
        get => stream.Position;
        set => throw _ThrowDisposedOrNotSupported();
    }

    public override void Flush()
    {
        stream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return stream.Read(buffer, offset, count);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        stream.Write(buffer, offset, count);
    }

    public override void Close()
    {
        stream.Close();
        base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        stream.Dispose();
        base.Dispose(disposing);
    }

    #region Not Supported

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    public override void SetLength(long value)
    {
        throw _ThrowDisposedOrNotSupported();
    }

    [DoesNotReturn]
    private Exception _ThrowDisposedOrNotSupported()
    {
        Ensure.NotDisposed(IsDisposed, this);

        throw new NotSupportedException();
    }

    #endregion
}
