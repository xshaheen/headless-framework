// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Http;
using Headless.Imaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetVips;

namespace Tests;

public sealed class NetVipsImageResizerContributorTests : TestBase
{
    #region Resize modes

    // Source: a 400 x 200 PNG. Each row is a mode, the requested box, and the output the mode promises.
    [Theory]
    [InlineData(ImageResizeMode.Stretch, 100, 100, 100, 100)]
    [InlineData(ImageResizeMode.Max, 100, 100, 100, 50)]
    [InlineData(ImageResizeMode.Max, 800, 800, 400, 200)] // never upscales
    [InlineData(ImageResizeMode.Crop, 100, 100, 100, 100)]
    [InlineData(ImageResizeMode.Crop, 800, 800, 800, 800)] // fills the box, upscaling if it must
    [InlineData(ImageResizeMode.Pad, 100, 100, 100, 100)]
    [InlineData(ImageResizeMode.BoxPad, 800, 800, 800, 800)] // source fits: padded, not resized
    [InlineData(ImageResizeMode.BoxPad, 100, 100, 100, 100)] // source larger: behaves like Pad
    [InlineData(ImageResizeMode.Min, 100, 100, 200, 100)] // shortest side lands on the target
    [InlineData(ImageResizeMode.Min, 500, 100, 400, 200)] // would upscale: original size kept
    public async Task should_produce_the_promised_size_for_each_resize_mode(
        ImageResizeMode mode,
        int width,
        int height,
        int expectedWidth,
        int expectedHeight
    )
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".png", 400, 200));

        // when
        var result = await _CreateResizer().TryResizeAsync(input, new ImageResizeArgs(mode, width, height), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Png);
        result.Result.Width.Should().Be(expectedWidth);
        result.Result.Height.Should().Be(expectedHeight);

        using var output = TestImages.Decode(result.Result.Content);
        output.Width.Should().Be(expectedWidth);
        output.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public async Task should_land_the_nearest_side_on_its_target_for_min_when_the_box_shape_differs()
    {
        // given: 100 x 1000; the width target (50) is nearer its source length than the height target (900)
        await using var input = new MemoryStream(TestImages.Encode(".png", 100, 1000));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Min, 50, 900), AbortToken);

        // then: the width lands on 50 and the height follows the aspect ratio, as ImageResizeMode.Min documents
        result.Result!.Width.Should().Be(50);
        result.Result.Height.Should().Be(500);
    }

    [Fact]
    public async Task should_not_count_an_unfilled_max_box_against_the_pixel_limit()
    {
        // given: a 20 000 x 20 000 box is above the default limit, but Max never grows a 400 x 200 source
        await using var input = new MemoryStream(TestImages.Encode(".png", 400, 200));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 20_000, 20_000), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Width.Should().Be(400);
        result.Result.Height.Should().Be(200);
    }

    [Fact]
    public async Task should_pad_a_16_bit_image_as_8_bit_srgb()
    {
        // given: a 16-bit RGB PNG; libvips thumbnail always produces 8-bit sRGB
        using var noise = TestImages.Noise(400, 200);
        using var wide = noise.Cast(Enums.BandFormat.Ushort).Copy(interpretation: Enums.Interpretation.Rgb16);
        using var scaled = wide * 256;
        using var sixteen = scaled.Cast(Enums.BandFormat.Ushort).Copy(interpretation: Enums.Interpretation.Rgb16);
        await using var input = new MemoryStream(sixteen.PngsaveBuffer(bitdepth: 16));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Pad, 100, 100), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        output.Bands.Should().Be(4);
        output.Getpoint(50, 5)[3].Should().Be(0);
        output.Format.Should().Be(Enums.BandFormat.Uchar);
        output.Getpoint(50, 50)[3].Should().Be(255);
    }

    [Fact]
    public async Task should_pad_with_transparency_when_the_format_has_alpha()
    {
        // given: 400 x 200 fitted into 100 x 100 leaves 25-pixel bands above and below
        await using var input = new MemoryStream(TestImages.Encode(".png", 400, 200));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Pad, 100, 100), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        output.Bands.Should().Be(4);
        output.Getpoint(50, 5)[3].Should().Be(0); // padding band
        output.Getpoint(50, 50)[3].Should().Be(255); // image
    }

    [Fact]
    public async Task should_pad_with_white_when_the_format_has_no_alpha()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 400, 200));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.BoxPad, 800, 800), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        output.Bands.Should().Be(3);
        output.Getpoint(5, 5).Should().AllSatisfy(channel => channel.Should().BeGreaterThan(240));
    }

    [Theory]
    [InlineData(ImageResizeMode.Max, 200, null, 200, 100)]
    [InlineData(ImageResizeMode.Crop, null, 50, 100, 50)]
    [InlineData(ImageResizeMode.Pad, 100, null, 100, 50)]
    public async Task should_derive_the_missing_side_from_the_aspect_ratio(
        ImageResizeMode mode,
        int? width,
        int? height,
        int expectedWidth,
        int expectedHeight
    )
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".png", 400, 200));
        var args = width is null
            ? new ImageResizeArgs(mode, width, height!.Value)
            : new ImageResizeArgs(mode, width.Value, height);

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.Width.Should().Be(expectedWidth);
        result.Result.Height.Should().Be(expectedHeight);
    }

    [Theory]
    [InlineData(NetVipsCropFocus.Center)]
    [InlineData(NetVipsCropFocus.Attention)]
    [InlineData(NetVipsCropFocus.Entropy)]
    public async Task should_crop_to_the_exact_box_with_every_crop_focus(NetVipsCropFocus focus)
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 400, 200));
        var resizer = _CreateResizer(new NetVipsOptions { CropFocus = focus });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Crop, 64, 64), AbortToken);

        // then
        result.Result!.Width.Should().Be(64);
        result.Result.Height.Should().Be(64);
    }

    [Fact]
    public async Task should_match_the_documented_min_size_on_a_real_photo()
    {
        // given: a 4391 x 3833 camera JPEG
        await using var input = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(
                input,
                new ImageResizeArgs(ImageResizeMode.Min, 344, 300, ContentTypes.Images.Jpeg),
                AbortToken
            );

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Jpeg);
        result.Result.Width.Should().Be(344);
        result.Result.Height.Should().Be(300);
    }

    [Theory]
    [InlineData(ImageResizeMode.None)]
    [InlineData(ImageResizeMode.Default)]
    public async Task should_return_the_caller_stream_unchanged_when_the_mode_does_not_resize(ImageResizeMode mode)
    {
        // given
        var original = TestImages.Encode(".jpg", 120, 80);
        await using var input = new MemoryStream(original);
        var args = new ImageResizeArgs(ImageResizeMode.Max, 10, 10) { Mode = mode };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Content.Should().BeSameAs(input);
        input.Position.Should().Be(0);
        input.ToArray().Should().Equal(original);
        result.Result.Width.Should().Be(120);
        result.Result.Height.Should().Be(80);
    }

    #endregion

    #region Formats

    [Theory]
    [InlineData(".jpg", "VipsForeignLoadJpegBuffer", ContentTypes.Images.Jpeg)]
    [InlineData(".png", "VipsForeignLoadPngBuffer", ContentTypes.Images.Png)]
    [InlineData(".webp", "VipsForeignLoadWebpBuffer", ContentTypes.Images.Webp)]
    [InlineData(".gif", "VipsForeignLoadNsgifBuffer", ContentTypes.Images.Gif)]
    [InlineData(".tif", "VipsForeignLoadTiffBuffer", ContentTypes.Images.Tiff)]
    [InlineData(".avif", "VipsForeignLoadHeifBuffer", ContentTypes.Images.Avif)]
    public async Task should_resize_each_supported_format_into_the_same_format(
        string suffix,
        string expectedLoader,
        string expectedMimeType
    )
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(suffix, 120, 80));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 60, 60), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.MimeType.Should().Be(expectedMimeType);
        result.Result.Width.Should().Be(60);
        result.Result.Height.Should().Be(40);
        TestImages.LoaderOf(result.Result.Content).Should().Be(expectedLoader);
    }

    [Fact]
    public async Task should_report_the_detected_format_when_the_mime_type_hint_disagrees()
    {
        // given: the hint names PNG, the bytes are JPEG
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 120, 80));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 60, 60, ContentTypes.Images.Png);

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.MimeType.Should().Be(ContentTypes.Images.Jpeg);
        TestImages.LoaderOf(result.Result.Content).Should().Be("VipsForeignLoadJpegBuffer");
    }

    [Theory]
    [InlineData(".gif", ImageResizeMode.Crop, 10, 10)]
    [InlineData(".gif", ImageResizeMode.Pad, 20, 20)]
    [InlineData(".gif", ImageResizeMode.Stretch, 10, 30)]
    [InlineData(".webp", ImageResizeMode.Crop, 10, 10)]
    [InlineData(".webp", ImageResizeMode.Pad, 20, 20)]
    public async Task should_resize_every_frame_of_an_animation(
        string suffix,
        ImageResizeMode mode,
        int width,
        int height
    )
    {
        // given: three solid 40 x 20 frames, red, green, blue; a content-aware focus must not move the crop per frame
        var animation = string.Equals(suffix, ".gif", StringComparison.Ordinal)
            ? TestImages.AnimatedGif(40, 20)
            : TestImages.AnimatedWebp(40, 20);
        await using var input = new MemoryStream(animation);
        var resizer = _CreateResizer(new NetVipsOptions { CropFocus = NetVipsCropFocus.Attention });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(mode, width, height), AbortToken);

        // then
        result.Result!.Width.Should().Be(width);
        result.Result.Height.Should().Be(height);

        using var output = TestImages.Decode(result.Result.Content, allFrames: true);
        output.PageHeight.Should().Be(height);
        output.Height.Should().Be(height * 3);
        ((int[])output.Get("delay")).Should().Equal(100, 200, 300);

        // Each frame keeps its own colour at its centre, so no frame was cropped or padded into its neighbour.
        output.Getpoint(width / 2, height / 2)[0].Should().BeGreaterThan(200);
        output.Getpoint(width / 2, height + (height / 2))[1].Should().BeGreaterThan(200);
        output.Getpoint(width / 2, (2 * height) + (height / 2))[2].Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task should_apply_the_exif_orientation_before_resizing()
    {
        // given: stored 80 x 40 on its side, displayed 40 x 80
        await using var input = new MemoryStream(TestImages.SidewaysJpeg(80, 40));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Stretch, 20, null), AbortToken);

        // then: the derived height follows the upright 1:2 shape, and the output is stored upright
        result.Result!.Width.Should().Be(20);
        result.Result.Height.Should().Be(40);

        using var output = TestImages.Decode(result.Result.Content);
        output.Width.Should().Be(20);
        output.Height.Should().Be(40);
    }

    [Fact]
    public async Task should_report_the_displayed_size_when_passing_a_sideways_photo_through()
    {
        // given: stored 80 x 40 with EXIF orientation 6, displayed 40 x 80
        await using var input = new MemoryStream(TestImages.SidewaysJpeg(80, 40));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 10, 10) { Mode = ImageResizeMode.None };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.Content.Should().BeSameAs(input);
        result.Result.Width.Should().Be(40);
        result.Result.Height.Should().Be(80);
    }

    #endregion

    #region Output format

    [Theory]
    [InlineData(".jpg", ContentTypes.Images.Webp, "VipsForeignLoadWebpBuffer")]
    [InlineData(".png", ContentTypes.Images.Avif, "VipsForeignLoadHeifBuffer")]
    [InlineData(".webp", ContentTypes.Images.Jpeg, "VipsForeignLoadJpegBuffer")]
    [InlineData(".tif", ContentTypes.Images.Png, "VipsForeignLoadPngBuffer")]
    [InlineData(".avif", ContentTypes.Images.Gif, "VipsForeignLoadNsgifBuffer")]
    public async Task should_encode_the_requested_output_format(string suffix, string outputMimeType, string loader)
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(suffix, 120, 80));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 60, 60) { OutputMimeType = outputMimeType };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.MimeType.Should().Be(outputMimeType);
        result.Result.Width.Should().Be(60);
        result.Result.Height.Should().Be(40);
        TestImages.LoaderOf(result.Result.Content).Should().Be(loader);
    }

    [Fact]
    public async Task should_convert_without_resizing_when_the_mode_does_not_resize()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".png", 120, 80));
        var args = new ImageResizeArgs(ContentTypes.Images.Png) { OutputMimeType = ContentTypes.Images.Webp };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.Content.Should().NotBeSameAs(input);
        result.Result.MimeType.Should().Be(ContentTypes.Images.Webp);
        result.Result.Width.Should().Be(120);
        result.Result.Height.Should().Be(80);
        TestImages.LoaderOf(result.Result.Content).Should().Be("VipsForeignLoadWebpBuffer");
    }

    [Fact]
    public async Task should_flatten_transparency_onto_white_when_converting_to_jpeg()
    {
        // given: a fully transparent PNG
        using var transparent = NetVips.Image.Black(40, 40, bands: 4).Copy(interpretation: Enums.Interpretation.Srgb);
        using var bytes = transparent.Cast(Enums.BandFormat.Uchar);
        await using var input = new MemoryStream(bytes.PngsaveBuffer());
        var args = new ImageResizeArgs(ImageResizeMode.Max, 40, 40) { OutputMimeType = ContentTypes.Images.Jpeg };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        output.Bands.Should().Be(3);
        output.Getpoint(20, 20).Should().AllSatisfy(channel => channel.Should().BeGreaterThan(240));
    }

    [Fact]
    public async Task should_keep_every_frame_when_converting_an_animation_to_webp()
    {
        // given
        await using var input = new MemoryStream(TestImages.AnimatedGif(40, 20));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 20, 20) { OutputMimeType = ContentTypes.Images.Webp };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content, allFrames: true);
        output.PageHeight.Should().Be(10);
        output.Height.Should().Be(30);
    }

    [Fact]
    public async Task should_keep_every_frame_when_converting_an_animated_webp_to_gif()
    {
        // given
        await using var input = new MemoryStream(TestImages.AnimatedWebp(40, 20));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 20, 20) { OutputMimeType = ContentTypes.Images.Gif };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content, allFrames: true);
        output.PageHeight.Should().Be(10);
        output.Height.Should().Be(30);
    }

    [Fact]
    public async Task should_keep_the_first_frame_when_converting_an_animation_to_a_still_format()
    {
        // given: red, green, then blue frames
        await using var input = new MemoryStream(TestImages.AnimatedGif(40, 20));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 40, 20) { OutputMimeType = ContentTypes.Images.Png };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.Height.Should().Be(20);

        using var output = TestImages.Decode(result.Result.Content);
        output.Height.Should().Be(20);
        output.Getpoint(20, 10)[0].Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task should_skip_an_output_format_it_cannot_write()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".png", 20, 20));
        var args = new ImageResizeArgs(ImageResizeMode.Max, 10, 10) { OutputMimeType = ContentTypes.Images.Bmp };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Contain(ContentTypes.Images.Bmp);
    }

    #endregion

    #region Encoder options

    [Fact]
    public async Task should_write_progressive_jpeg_when_configured()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 120, 80));
        var resizer = _CreateResizer(new NetVipsOptions { JpegProgressive = true });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 60, 60), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        ((int)output.Get("interlaced")).Should().Be(1);
    }

    [Theory]
    [InlineData(NetVipsChromaSubsampling.On, "4:2:0")]
    [InlineData(NetVipsChromaSubsampling.Off, "4:4:4")]
    public async Task should_apply_the_configured_chroma_subsampling(NetVipsChromaSubsampling mode, string expected)
    {
        // given: quality 95, where Auto would keep full colour
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 120, 80));
        var resizer = _CreateResizer(new NetVipsOptions { JpegQuality = 95, ChromaSubsampling = mode });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 60, 60), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        ((string)output.Get("jpeg-chroma-subsample")).Should().Be(expected);
    }

    #endregion

    #region Input reading

    [Fact]
    public async Task should_pass_through_after_reading_only_the_header()
    {
        // given: a 7 MB camera JPEG
        await using var file = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));
        await using var input = new CountingStream(file);
        var args = new ImageResizeArgs(ImageResizeMode.Max, 10, 10) { Mode = ImageResizeMode.None };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.Content.Should().BeSameAs(input);
        input.BytesRead.Should().BeLessThanOrEqualTo(256 * 1024);
    }

    [Fact]
    public async Task should_refuse_an_image_over_the_pixel_limit_after_reading_only_the_header()
    {
        // given: a 7 MB, 4391 x 3833 JPEG against a one-megapixel limit
        await using var file = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));
        await using var input = new CountingStream(file);
        var resizer = _CreateResizer(new NetVipsOptions { MaxPixels = 1_000_000 });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 10, 10), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Failed);
        input.BytesRead.Should().BeLessThanOrEqualTo(256 * 1024);
    }

    [Fact]
    public async Task should_never_read_an_unknown_stream_type_synchronously()
    {
        // given: a seekable stream that throws on synchronous reads, like a partly buffered ASP.NET Core request body
        await using var input = new AsyncOnlyStream(TestImages.Encode(".jpg", 400, 200));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 100, 100), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Width.Should().Be(100);
    }

    [Fact]
    public async Task should_copy_a_large_unknown_stream_asynchronously_before_libvips_reads_it()
    {
        // given: a 7 MB upload in a stream that throws on synchronous reads, larger than the first header window, so
        // the pixels must come through the asynchronous copy rather than the header read
        var bytes = await File.ReadAllBytesAsync(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"), AbortToken);
        await using var input = new AsyncOnlyStream(bytes);

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 300, 300), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Done);
        result.Result!.Width.Should().Be(300);
    }

    [Fact]
    public async Task should_pass_a_non_seekable_stream_through_as_a_copy_of_its_bytes()
    {
        // given: a non-seekable stream is read to its end, so the original cannot be handed back
        var original = TestImages.Encode(".png", 40, 20);
        await using var input = new NonSeekableStream(original);
        var args = new ImageResizeArgs(ImageResizeMode.Max, 10, 10) { Mode = ImageResizeMode.None };

        // when
        var result = await _CreateResizer().TryResizeAsync(input, args, AbortToken);

        // then
        result.Result!.Content.Should().NotBeSameAs(input);
        result.Result.Content.GetAllBytes().Should().Equal(original);
    }

    [Fact]
    public async Task should_not_keep_the_caller_streams_alive_after_resizing()
    {
        // given: libvips' operation cache is on (the default) and keeps some operations, with the source each read
        // from; without detaching, a few of these 40 streams stay reachable through it
        var bytes = await File.ReadAllBytesAsync(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"), AbortToken);
        var references = new List<WeakReference>();

        for (var i = 0; i < 40; i++)
        {
            references.Add(await _ResizeAndForgetAsync(bytes));
        }

        // when
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // then
        references.Should().AllSatisfy(reference => reference.IsAlive.Should().BeFalse());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> _ResizeAndForgetAsync(byte[] bytes)
    {
        var input = new MemoryStream(bytes);
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 100, 100), AbortToken);
        await result.Result!.Content.DisposeAsync();
        await input.DisposeAsync();

        return new WeakReference(input);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task should_stop_a_running_encode_when_cancelled()
    {
        // given: a 10 000 x 10 000 PNG at the slowest deflate level takes seconds to encode
        await using var input = new MemoryStream(TestImages.Encode(".png", 100, 100));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // when
        var act = async () =>
            await _CreateResizer()
                .TryResizeAsync(
                    input,
                    new ImageResizeArgs(ImageResizeMode.Stretch, 10_000, 10_000),
                    cancellation.Token
                );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task should_throw_before_reading_when_already_cancelled()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".png", 20, 20));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // when
        var act = async () =>
            await _CreateResizer()
                .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 10, 10), cancellation.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Invalid input

    [Fact]
    public async Task should_refuse_heic_because_the_bundled_libvips_cannot_decode_hevc()
    {
        // given
        await using var input = File.OpenRead(TestImages.AssetPath("hevc-64.heic"));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 10, 10), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
    }

    [Fact]
    public async Task should_refuse_bmp_because_the_bundled_libvips_has_no_bmp_loader()
    {
        // given
        await using var input = new MemoryStream(TestImages.Bmp);

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 1, 1), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("The encoded image format is unknown or not supported.");
    }

    [Fact]
    public async Task should_refuse_svg_even_though_libvips_can_load_it()
    {
        // given
        await using var input = new MemoryStream(TestImages.Svg);

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 5, 5), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    public async Task should_refuse_bytes_that_are_not_an_image(byte[] bytes)
    {
        // given
        await using var input = new MemoryStream(bytes);

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 5, 5), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
    }

    // JPEG takes libvips' shrink-on-load path, where a truncated file used to resize with its missing rows filled in.
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    public async Task should_refuse_a_truncated_image_as_invalid_content(string suffix)
    {
        // given
        var encoded = TestImages.Encode(suffix, 200, 200);
        await using var input = new MemoryStream(encoded[..(encoded.Length / 2)]);

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 50, 50), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Be("The encoded image contains invalid content.");
    }

    [Fact]
    public async Task should_skip_without_reading_when_the_mime_type_hint_is_unsupported()
    {
        // given
        await using var input = new MemoryStream(TestImages.Encode(".jpg", 20, 20));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 5, 5, ContentTypes.Images.Bmp), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Unsupported);
        result.Error.Should().Contain(ContentTypes.Images.Bmp);
        input.Position.Should().Be(0);
    }

    #endregion

    #region Secure defaults

    [Fact]
    public async Task should_fail_when_the_input_exceeds_the_pixel_limit()
    {
        // given: 100 x 100 is 10 000 pixels
        await using var input = new MemoryStream(TestImages.Encode(".png", 100, 100));
        var resizer = _CreateResizer(new NetVipsOptions { MaxPixels = 9_999 });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 10, 10), AbortToken);

        // then: Failed, not Unsupported, so the pipeline does not offer the image to another contributor
        result.State.Should().Be(ImageProcessState.Failed);
        result.Error.Should().Contain("10000 pixels");
    }

    [Fact]
    public async Task should_count_every_frame_against_the_pixel_limit()
    {
        // given: three 40 x 20 frames are 2 400 pixels, one frame is 800
        await using var input = new MemoryStream(TestImages.AnimatedGif(40, 20));
        var resizer = _CreateResizer(new NetVipsOptions { MaxPixels = 2_000 });

        // when
        var result = await resizer.TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 10, 10), AbortToken);

        // then
        result.State.Should().Be(ImageProcessState.Failed);
    }

    [Fact]
    public async Task should_fail_when_the_requested_output_exceeds_the_pixel_limit()
    {
        // given: a small input stretched to 1000 x 1000
        await using var input = new MemoryStream(TestImages.Encode(".png", 20, 20));
        var resizer = _CreateResizer(new NetVipsOptions { MaxPixels = 500_000 });

        // when
        var result = await resizer.TryResizeAsync(
            input,
            new ImageResizeArgs(ImageResizeMode.Stretch, 1000, 1000),
            AbortToken
        );

        // then
        result.State.Should().Be(ImageProcessState.Failed);
        result.Error.Should().Contain("1000000 pixels");
    }

    [Fact]
    public async Task should_strip_exif_by_default()
    {
        // given: a camera JPEG that carries EXIF
        await using var input = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));

        // when
        var result = await _CreateResizer()
            .TryResizeAsync(input, new ImageResizeArgs(ImageResizeMode.Max, 100, 100), AbortToken);

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        output.Contains("exif-data").Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_exif_when_metadata_stripping_is_off()
    {
        // given
        await using var input = File.OpenRead(TestImages.AssetPath("happy-young-man-with-q-letter.jpg"));
        var resizer = _CreateResizer(new NetVipsOptions { StripMetadata = false });

        // when
        var result = await resizer.TryResizeAsync(
            input,
            new ImageResizeArgs(ImageResizeMode.Max, 100, 100),
            AbortToken
        );

        // then
        using var output = TestImages.Decode(result.Result!.Content);
        output.Contains("exif-data").Should().BeTrue();
    }

    #endregion

    private static NetVipsImageResizerContributor _CreateResizer(NetVipsOptions? options = null)
    {
        return new NetVipsImageResizerContributor(
            Options.Create(options ?? new NetVipsOptions()),
            NullLogger<NetVipsImageResizerContributor>.Instance
        );
    }
}
