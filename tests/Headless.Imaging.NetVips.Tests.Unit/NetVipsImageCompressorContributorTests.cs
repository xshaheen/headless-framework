// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Http;
using Headless.Imaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class NetVipsImageCompressorContributorTests : TestBase
{
    // Each input is encoded wastefully (top quality, or no PNG deflate), so the default settings must shrink it.
    [Theory]
    [InlineData(".jpg", "Q=100", "VipsForeignLoadJpegBuffer")]
    [InlineData(".png", "compression=0", "VipsForeignLoadPngBuffer")]
    [InlineData(".webp", "Q=100", "VipsForeignLoadWebpBuffer")]
    [InlineData(".avif", "Q=100", "VipsForeignLoadHeifBuffer")]
    public async Task should_compress_each_supported_format_in_its_own_format(
        string suffix,
        string encodeOptions,
        string expectedLoader
    )
    {
        // given
        var original = TestImages.Encode(suffix, 128, 128, options: encodeOptions);
        await using var input = new MemoryStream(original);

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Length.Should().BeLessThan(original.Length);
        TestImages.LoaderOf(result.Result).Should().Be(expectedLoader);

        using var output = TestImages.Decode(result.Result);
        output.Width.Should().Be(128);
        output.Height.Should().Be(128);
    }

    [Fact]
    public async Task should_keep_every_frame_when_compressing_an_animated_webp()
    {
        // given: three 64 x 32 frames
        var original = TestImages.AnimatedWebp(64, 32);
        await using var input = new MemoryStream(original);

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Length.Should().BeLessThan(original.Length);

        using var output = TestImages.Decode(result.Result, allFrames: true);
        output.PageHeight.Should().Be(32);
        output.Height.Should().Be(96);
        ((int[])output.Get("delay")).Should().Equal(100, 200, 300);
    }

    [Fact]
    public async Task should_compress_into_the_requested_format()
    {
        // given
        var original = TestImages.Encode(".jpg", 128, 128, options: "Q=100");
        await using var input = new MemoryStream(original);
        var args = new ImageCompressArgs { OutputMimeType = ContentTypes.Images.Webp };

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Length.Should().BeLessThan(original.Length);
        TestImages.LoaderOf(result.Result).Should().Be("VipsForeignLoadWebpBuffer");
    }

    [Fact]
    public async Task should_compress_an_animated_gif_into_an_animated_webp()
    {
        // given: GIF has no quality setting of its own, so it compresses only into another format
        var original = TestImages.AnimatedNoiseGif(64, 32);
        await using var input = new MemoryStream(original);
        var args = new ImageCompressArgs(ContentTypes.Images.Gif) { OutputMimeType = ContentTypes.Images.Webp };

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);

        using var output = TestImages.Decode(result.Result!, allFrames: true);
        output.PageHeight.Should().Be(32);
        output.Height.Should().Be(96);
    }

    [Theory]
    [InlineData(ContentTypes.Images.Gif)]
    [InlineData(ContentTypes.Images.Tiff)]
    [InlineData(ContentTypes.Images.Bmp)]
    public async Task should_skip_an_output_format_without_a_quality_setting(string outputMimeType)
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 32, 32));
        var args = new ImageCompressArgs { OutputMimeType = outputMimeType };

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Contain(outputMimeType);
    }

    [Fact]
    public async Task should_flatten_a_16_bit_image_onto_white_when_converting_to_jpeg()
    {
        // given: a fully transparent 16-bit RGBA PNG; compression keeps the source depth until the JPEG encoder
        using var black = NetVips.Image.Black(40, 40, bands: 4);
        using var wide = black
            .Cast(NetVips.Enums.BandFormat.Ushort)
            .Copy(interpretation: NetVips.Enums.Interpretation.Rgb16);
        await using var input = new MemoryStream(wide.PngsaveBuffer(bitdepth: 16));
        var args = new ImageCompressArgs { OutputMimeType = ContentTypes.Images.Jpeg };

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, args, AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!);
        output.Getpoint(20, 20).Should().AllSatisfy(channel => channel.Should().BeGreaterThan(240));
    }

    [Fact]
    public async Task should_never_read_an_unknown_stream_type_synchronously()
    {
        // given: a seekable stream that throws on synchronous reads, like a partly buffered ASP.NET Core request body
        var original = TestImages.Encode(".jpg", 128, 128, options: "Q=100");
        await using var input = new AsyncOnlyStream(original);

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Length.Should().BeLessThan(original.Length);
    }

    [Fact]
    public async Task should_compress_a_file_stream_read_by_libvips_directly()
    {
        // given: a FileStream is read by libvips itself, without a copy
        await using var input = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Length.Should().BeLessThan(input.Length);
    }

    [Fact]
    public async Task should_throw_when_cancelled()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 32, 32, options: "Q=100"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // when
        var act = async () =>
            await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_compress_a_real_photo()
    {
        // given: a 7 MB camera JPEG
        await using var input = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));

        // when
        var result = await _CreateCompressor()
            .TryCompressAsync(input, new ImageCompressArgs(ContentTypes.Images.Jpeg), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Length.Should().BeLessThan(input.Length);
    }

    [Fact]
    public async Task should_fail_when_the_output_is_not_smaller_than_the_input()
    {
        // given: a quality-50 JPEG re-encoded at quality 100 can only grow
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 128, 128, options: "Q=50"));
        var compressor = _CreateCompressor(new NetVipsOptions { JpegQuality = 100 });

        // when
        var result = await compressor.TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Failed);
        result.Error.Should().Be("The compressed image is larger than the original.");
        result.Result.Should().BeNull();
    }

    [Fact]
    public async Task should_apply_the_configured_quality()
    {
        // given
        var original = TestImages.Encode(".jpg", 128, 128, options: "Q=100");
        await using var lowInput = new MemoryStream(original);
        await using var highInput = new MemoryStream(original);

        // when
        var low = await _CreateCompressor(new NetVipsOptions { JpegQuality = 20 })
            .TryCompressAsync(lowInput, new ImageCompressArgs(), AbortToken);
        var high = await _CreateCompressor(new NetVipsOptions { JpegQuality = 90 })
            .TryCompressAsync(highInput, new ImageCompressArgs(), AbortToken);

        // then
        low.Result!.Length.Should().BeLessThan(high.Result!.Length);
    }

    [Theory]
    [InlineData(".gif", ContentTypes.Images.Gif)]
    [InlineData(".tif", ContentTypes.Images.Tiff)]
    public async Task should_refuse_formats_without_a_quality_setting(string suffix, string mimeType)
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(suffix, 32, 32));

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Contain(mimeType);
    }

    [Theory]
    [InlineData(ContentTypes.Images.Gif)]
    [InlineData(ContentTypes.Images.Bmp)]
    public async Task should_skip_when_the_mime_type_hint_is_not_compressible(string mimeType)
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 32, 32));

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(mimeType), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Contain(mimeType);
    }

    [Fact]
    public async Task should_refuse_a_format_outside_the_allowlist()
    {
        // given
        await using var input = new MemoryStream(TestImages.Svg);

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
    }

    [Fact]
    public async Task should_refuse_a_truncated_image_as_invalid_content()
    {
        // given
        var jpeg = TestImages.Encode(".jpg", 200, 200, options: "Q=100");
        await using var input = new MemoryStream(jpeg[..(jpeg.Length / 2)]);

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("The encoded image contains invalid content.");
    }

    [Fact]
    public async Task should_fail_when_the_input_exceeds_the_pixel_limit()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 100, 100, options: "Q=100"));
        var compressor = _CreateCompressor(new NetVipsOptions { MaxPixels = 9_999 });

        // when
        var result = await compressor.TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Failed);
    }

    [Fact]
    public async Task should_turn_the_pixels_upright_when_stripping_the_orientation()
    {
        // given: stored 80 x 40 on its side, displayed 40 x 80
        await using var input = new MemoryStream(TestImages.SidewaysJpeg(80, 40));

        // when
        var result = await _CreateCompressor().TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!);
        output.Contains("exif-data").Should().BeFalse();
        output.Width.Should().Be(40);
        output.Height.Should().Be(80);
    }

    [Fact]
    public async Task should_keep_the_orientation_tag_when_metadata_stripping_is_off()
    {
        // given
        await using var input = new MemoryStream(TestImages.SidewaysJpeg(80, 40));
        var compressor = _CreateCompressor(new NetVipsOptions { StripMetadata = false, JpegQuality = 40 });

        // when
        var result = await compressor.TryCompressAsync(input, new ImageCompressArgs(), AbortToken);

        // then: pixels stay as stored, and the tag still turns them for display
        using var output = TestImages.Decode(result.Result!);
        output.Width.Should().Be(80);
        output.Height.Should().Be(40);
        ((int)output.Get("orientation")).Should().Be(6);
    }

    private static NetVipsImageCompressorContributor _CreateCompressor(NetVipsOptions? options = null)
    {
        return new NetVipsImageCompressorContributor(
            Options.Create(options ?? new NetVipsOptions()),
            NullLogger<NetVipsImageCompressorContributor>.Instance
        );
    }
}
