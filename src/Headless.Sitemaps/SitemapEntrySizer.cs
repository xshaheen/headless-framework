// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;

namespace Headless.Sitemaps;

/// <summary>
/// Measures the exact UTF-8 bytes one entry adds to a sitemap file, by writing it with the same
/// <see cref="XmlWriter"/> settings and entry writer as the real file into a stream that only counts. Escaping,
/// indentation, and multi-byte characters are therefore counted as written, never estimated.
/// </summary>
/// <remarks>
/// The root element declares every namespace prefix an entry may use, so <see cref="DocumentOverhead"/> is the
/// largest prolog and closing tag a file can have, and a size computed from it never undercounts.
/// </remarks>
internal sealed class SitemapEntrySizer<T> : IAsyncDisposable
{
    private readonly ByteCountingStream _stream;
    private readonly XmlWriter _writer;
    private readonly Func<XmlWriter, T, CancellationToken, Task> _writeEntry;

    private SitemapEntrySizer(
        ByteCountingStream stream,
        XmlWriter writer,
        Func<XmlWriter, T, CancellationToken, Task> writeEntry,
        long documentOverhead
    )
    {
        _stream = stream;
        _writer = writer;
        _writeEntry = writeEntry;
        DocumentOverhead = documentOverhead;
    }

    /// <summary>The bytes of a file holding no entries: XML declaration, root start tag, and root end tag.</summary>
    public long DocumentOverhead { get; }

    /// <summary>Creates a sizer whose files start with <paramref name="writeRoot"/> and hold <paramref name="writeEntry"/> entries.</summary>
    /// <param name="writeRoot">Writes the XML declaration and the root start element with its namespace attributes.</param>
    /// <param name="writeEntry">Writes one entry exactly as the real file writes it.</param>
    /// <param name="sample">
    /// Any entry. It is written before measuring starts, so the first measured entry does not absorb the
    /// <c>&gt;</c> that closes the root start tag.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task<SitemapEntrySizer<T>> CreateAsync(
        Func<XmlWriter, Task> writeRoot,
        Func<XmlWriter, T, CancellationToken, Task> writeEntry,
        T sample,
        CancellationToken cancellationToken
    )
    {
        // A complete document with the sample written twice: total = overhead + 2 x one entry's steady-state size.
        var probeStream = new ByteCountingStream();
        long entryBytes;

        await using (var probe = XmlWriter.Create(probeStream, SitemapConstants.WriterSettings))
        {
            await writeRoot(probe).ConfigureAwait(false);
            await writeEntry(probe, sample, cancellationToken).ConfigureAwait(false);
            await probe.FlushAsync().ConfigureAwait(false);
            var afterFirst = probeStream.Length;
            await writeEntry(probe, sample, cancellationToken).ConfigureAwait(false);
            await probe.FlushAsync().ConfigureAwait(false);
            entryBytes = probeStream.Length - afterFirst;
            await probe.WriteEndElementAsync().ConfigureAwait(false);
        }

        var documentOverhead = probeStream.Length - (2 * entryBytes);

        var stream = new ByteCountingStream();
        var writer = XmlWriter.Create(stream, SitemapConstants.WriterSettings);

        try
        {
            await writeRoot(writer).ConfigureAwait(false);
            await writeEntry(writer, sample, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new SitemapEntrySizer<T>(stream, writer, writeEntry, documentOverhead);
    }

    /// <summary>Returns the bytes <paramref name="entry"/> adds to a file after its first entry.</summary>
    public async ValueTask<long> MeasureAsync(T entry, CancellationToken cancellationToken)
    {
        var before = _stream.Length;
        await _writeEntry(_writer, entry, cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);

        return _stream.Length - before;
    }

    public ValueTask DisposeAsync()
    {
        return _writer.DisposeAsync();
    }

    // Discards the bytes and keeps their count, so measuring a 50 MB file holds no buffer.
    private sealed class ByteCountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _length += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _length += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _length += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _length += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
