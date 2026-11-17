using System.IO.Compression;
using System.Text;
using Headless.IO;
using Headless.Media.Indexing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PdfMediaFileTextProviderTests : TestBase
{
    private readonly PdfMediaFileTextProvider _sut = new();

    [Fact]
    public async Task should_extract_text_from_real_pdf_file_when_get_text_async()
    {
        // given
        var separator = Path.DirectorySeparatorChar;
        var pdfFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            $"..{separator}..{separator}..{separator}Files{separator}TestPdf.pdf"
        );
        await using var fileStream = File.OpenRead(pdfFilePath);

        // when
        var result = await _sut.GetTextAsync(fileStream, cancellationToken: AbortToken);

        // then
        await Verify(result).UseDirectory("Snapshots");
    }

    [Fact]
    public async Task should_extract_the_same_text_from_a_non_seekable_stream_of_known_length()
    {
        // given
        var separator = Path.DirectorySeparatorChar;
        var pdfFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            $"..{separator}..{separator}..{separator}Files{separator}TestPdf.pdf"
        );
        await using var seekable = File.OpenRead(pdfFilePath);
        var expected = await _sut.GetTextAsync(seekable, cancellationToken: AbortToken);
        await using var nonSeekable = new NonSeekableStream(File.OpenRead(pdfFilePath));

        // when
        var result = await _sut.GetTextAsync(nonSeekable, new FileInfo(pdfFilePath).Length, AbortToken);

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public async Task should_refuse_a_non_seekable_stream_past_the_buffer_limit()
    {
        // given: 101 MiB, just past the 100 MiB the provider buffers
        await using var stream = new ZeroStream(101L * 1024 * 1024);

        // when
        var act = async () => await _sut.GetTextAsync(stream, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task should_extract_text_from_a_flate_encoded_page_within_the_decode_limit()
    {
        // given: a page whose content stream is Flate-encoded, as almost every real PDF's is
        await using var pdf = _PdfWithFlatePage(static zlib =>
            zlib.Write("BT /F1 12 Tf 72 712 Td (Bounded hello) Tj ET"u8)
        );

        // when
        var result = await _sut.GetTextAsync(pdf, cancellationToken: AbortToken);

        // then
        result.Should().Be("Bounded hello");
    }

    [Fact]
    public async Task should_refuse_a_flate_stream_that_inflates_past_the_decode_limit()
    {
        // given: about 100 KiB of PDF whose page content inflates to 101 MiB, just past the 100 MiB limit
        await using var pdf = _PdfWithFlatePage(static zlib =>
        {
            var spaces = new byte[1024 * 1024];
            spaces.AsSpan().Fill((byte)' ');

            for (var i = 0; i < 101; i++)
            {
                zlib.Write(spaces);
            }
        });
        pdf.Length.Should().BeLessThan(1024 * 1024);

        // when
        var act = async () => await _sut.GetTextAsync(pdf, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*Flate*");
    }

    /// <summary>
    /// Builds a one-page PDF whose content stream is the zlib data <paramref name="writeContent"/> writes, using a
    /// standard font so no font program is embedded.
    /// </summary>
    private static MemoryStream _PdfWithFlatePage(Action<ZLibStream> writeContent)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            writeContent(zlib);
        }

        var pdf = new MemoryStream();
        var offsets = new List<long>();

        void write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));

        void writeObject(string body)
        {
            offsets.Add(pdf.Position);
            write($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        write("%PDF-1.7\n");
        writeObject("<< /Type /Catalog /Pages 2 0 R >>");
        writeObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writeObject(
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R "
                + "/Resources << /Font << /F1 5 0 R >> >> >>"
        );
        offsets.Add(pdf.Position);
        write($"4 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
        compressed.Position = 0;
        compressed.CopyTo(pdf);
        write("\nendstream\nendobj\n");
        writeObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        var xref = pdf.Position;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        table.Append(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"
        );
        write(table.ToString());
        pdf.Position = 0;

        return pdf;
    }

    /// <summary>A non-seekable stream of <paramref name="length"/> zero bytes that it never holds in memory.</summary>
    private sealed class ZeroStream(long length) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var read = (int)Math.Min(buffer.Length, _remaining);
            buffer[..read].Clear();
            _remaining -= read;

            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
