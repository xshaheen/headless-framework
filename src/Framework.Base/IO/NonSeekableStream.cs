// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.IO;

public sealed class NonSeekableStream(Stream stream) : Stream
{
    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => stream.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => stream.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        stream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return stream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
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
        stream.Dispose();
        base.Dispose(disposing);
    }
}
