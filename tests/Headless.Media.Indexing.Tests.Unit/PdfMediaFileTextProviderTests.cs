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
        var result = await _sut.GetTextAsync(fileStream, AbortToken);

        // then
        await Verify(result).UseDirectory("Snapshots");
    }

    [Fact]
    public async Task should_refuse_a_non_seekable_stream_past_the_buffer_limit()
    {
        // given: 101 MiB, just past the 100 MiB the provider buffers
        await using var stream = new ZeroStream(101L * 1024 * 1024);

        // when
        var act = async () => await _sut.GetTextAsync(stream, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidDataException>();
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
