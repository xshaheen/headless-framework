// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Http;
using Headless.Imaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class NetVipsImageInspectorContributorTests : TestBase
{
    [Theory]
    [InlineData(".jpg", ContentTypes.Images.Jpeg, false)]
    [InlineData(".png", ContentTypes.Images.Png, false)]
    [InlineData(".webp", ContentTypes.Images.Webp, false)]
    [InlineData(".tif", ContentTypes.Images.Tiff, false)]
    [InlineData(".avif", ContentTypes.Images.Avif, false)]
    [InlineData(".png", ContentTypes.Images.Png, true)]
    [InlineData(".webp", ContentTypes.Images.Webp, true)]
    public async Task should_report_the_format_size_and_transparency_of_each_supported_format(
        string suffix,
        string expectedMimeType,
        bool alpha
    )
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(suffix, 120, 80, alpha));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result
            .Result.Should()
            .Be(
                new ImageInfo
                {
                    MimeType = expectedMimeType,
                    Width = 120,
                    Height = 80,
                    FrameCount = 1,
                    HasAlpha = alpha,
                }
            );
        result.Result!.IsAnimated.Should().BeFalse();
    }

    [Theory]
    [InlineData(".gif", ContentTypes.Images.Gif)]
    [InlineData(".webp", ContentTypes.Images.Webp)]
    public async Task should_count_the_frames_of_an_animation_and_report_one_frame_size(string suffix, string mimeType)
    {
        // given: three 40 x 20 frames
        var animation = string.Equals(suffix, ".gif", StringComparison.Ordinal)
            ? TestImages.AnimatedGif(40, 20)
            : TestImages.AnimatedWebp(40, 20);
        await using var input = new MemoryStream(animation);

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.Result!.MimeType.Should().Be(mimeType);
        result.Result.Width.Should().Be(40);
        result.Result.Height.Should().Be(20);
        result.Result.FrameCount.Should().Be(3);
        result.Result.IsAnimated.Should().BeTrue();
    }

    [Fact]
    public async Task should_report_the_displayed_size_of_a_sideways_photo()
    {
        // given: stored 80 x 40 with EXIF orientation 6, displayed 40 x 80
        await using var input = new MemoryStream(TestImages.SidewaysJpeg(80, 40));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.Result!.Width.Should().Be(40);
        result.Result.Height.Should().Be(80);
    }

    [Fact]
    public async Task should_read_only_the_header_of_a_large_photo()
    {
        // given: a 7 MB camera JPEG whose header sits in the first kilobytes
        await using var file = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));
        await using var input = new CountingStream(file);

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.Result!.Width.Should().Be(4391);
        result.Result.Height.Should().Be(3833);
        input.BytesRead.Should().BeLessThanOrEqualTo(256 * 1024);
    }

    [Fact]
    public async Task should_grow_the_window_until_the_header_parses()
    {
        // given: libtiff writes the image directory after the pixel data, past the first 64 KB window
        var tiff = TestImages.Encode(".tif", 512, 512);
        await using var input = new CountingStream(new MemoryStream(tiff));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Width.Should().Be(512);
        input.BytesRead.Should().BeGreaterThan(64 * 1024);
    }

    [Fact]
    public async Task should_refuse_an_unknown_format_from_the_first_window()
    {
        // given: 1 MB of bytes no allowed format starts with
        var bytes = new byte[1024 * 1024];
        bytes.AsSpan().Fill(0x5A);
        await using var input = new CountingStream(new MemoryStream(bytes));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        input.BytesRead.Should().Be(64 * 1024);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task should_read_from_the_start_wherever_the_position_was_left(int pastEnd)
    {
        // given: a caller that copied the upload in and left the position at, or past, its end
        await using var input = new MemoryStream();
        await input.WriteAsync(TestImages.Encode(".png", 30, 20), AbortToken);
        input.Position = input.Length + pastEnd;

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.Result!.Width.Should().Be(30);
    }

    [Fact]
    public async Task should_report_one_frame_for_a_multi_page_tiff()
    {
        // given: a two-page TIFF; pages of a document are not animation frames
        using var first = TestImages.Noise(40, 20);
        using var second = TestImages.Noise(40, 20);
        using var strip = NetVips.Image.Arrayjoin([first, second], across: 1);
        using var paged = strip.Mutate(image => image.Set(NetVips.GValue.GIntType, "page-height", 20));
        await using var input = new MemoryStream(paged.TiffsaveBuffer());

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.Result!.FrameCount.Should().Be(1);
        result.Result.Height.Should().Be(20);
    }

    [Fact]
    public async Task should_inspect_a_non_seekable_stream()
    {
        // given
        await using var input = new NonSeekableStream(TestImages.Encode(".png", 30, 20));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.Result!.Width.Should().Be(30);
        result.Result.Height.Should().Be(20);
    }

    [Fact]
    public async Task should_refuse_heic_because_the_pipeline_cannot_process_it()
    {
        // given: an HEVC-coded HEIF from macOS sips; the bundled libvips cannot decode HEVC
        await using var input = File.OpenRead(TestImages.AssetPath("hevc-64.heic"));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("The encoded image format is unknown or not supported.");
    }

    public static TheoryData<byte[]> RefusedInputs =>
        [TestImages.Bmp, TestImages.Svg, Array.Empty<byte>(), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }];

    [Theory]
    [MemberData(nameof(RefusedInputs))]
    public async Task should_refuse_formats_outside_the_allowlist(byte[] bytes)
    {
        // given
        await using var input = new MemoryStream(bytes);

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
    }

    [Fact]
    public async Task should_refuse_a_file_whose_header_is_cut_short()
    {
        // given: a PNG signature with the start of its header chunk and nothing after it
        var png = TestImages.Encode(".png", 30, 20);
        await using var input = new MemoryStream(png[..20]);

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("The encoded image contains invalid content.");
    }

    [Fact]
    public async Task should_report_a_size_above_the_pixel_limit_without_refusing_it()
    {
        // given: 17 000 x 17 000 is above the default MaxPixels, but only the header is read
        using var black = NetVips.Image.Black(17_000, 17_000);
        await using var input = new MemoryStream(black.PngsaveBuffer(compression: 1));

        // when
        var result = await _CreateInspector().TryInspectAsync(input, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Width.Should().Be(17_000);
    }

    [Fact]
    public async Task should_throw_when_cancelled()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".png", 30, 20));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // when
        var act = async () => await _CreateInspector().TryInspectAsync(input, cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static NetVipsImageInspectorContributor _CreateInspector()
    {
        return new NetVipsImageInspectorContributor(NullLogger<NetVipsImageInspectorContributor>.Instance);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    /// <summary>Counts the bytes read through it, to prove the inspector stops at the header.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;

            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
