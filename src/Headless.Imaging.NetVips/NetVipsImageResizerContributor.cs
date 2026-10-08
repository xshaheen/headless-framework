// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetVips;

namespace Headless.Imaging;

/// <summary>
/// An <see cref="IImageResizerContributor" /> that resizes JPEG, PNG, WebP, GIF, TIFF, and AVIF images with libvips.
/// </summary>
/// <remarks>
/// <para>
/// The output keeps the source format, and its MIME type is the one detected from the bytes: the
/// <see cref="ImageResizeArgs.MimeType" /> hint only lets the contributor skip a type it does not handle. The resize
/// runs through libvips <c>thumbnail</c>, which shrinks JPEG and WebP while decoding and applies the EXIF orientation,
/// so the output is upright and its width and height are the displayed ones.
/// </para>
/// <para>
/// When <see cref="ImageResizeArgs.Mode" /> resolves to <see cref="ImageResizeMode.None" />, the caller's stream comes
/// back unchanged, rewound to its start, with the stored width and height read from the header.
/// </para>
/// </remarks>
internal sealed class NetVipsImageResizerContributor : IImageResizerContributor
{
    private readonly NetVipsOptions _options;
    private readonly ILogger<NetVipsImageResizerContributor> _logger;

    public NetVipsImageResizerContributor(
        IOptions<NetVipsOptions> optionsAccessor,
        ILogger<NetVipsImageResizerContributor> logger
    )
    {
        VipsRuntime.EnsureAvailable();

        _options = optionsAccessor.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(args.MimeType) && VipsFormat.FromMimeType(args.MimeType) is null)
        {
            return ImageStreamResizeResult.NotSupportedMimeType(args.MimeType);
        }

        var bytes = await stream.GetAllBytesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var (source, state, error) = VipsImageSource.Open(bytes, _options, _logger);

            if (source is null)
            {
                return state is ImageProcessState.Failed
                    ? ImageStreamResizeResult.Failed(error!)
                    : ImageStreamResizeResult.NotSupported(error!);
            }

            using (source)
            {
                if (args.Mode is ImageResizeMode.None or ImageResizeMode.Default)
                {
                    // A stream the contributor cannot rewind has been read to its end, so hand back the bytes instead.
                    var content = stream.CanSeek ? stream : new MemoryStream(bytes);
                    content.Position = 0;

                    return ImageStreamResizeResult.Done(content, source.Format.MimeType, source.Width, source.Height);
                }

                var plan = VipsResizePlan.Create(
                    args.Mode,
                    args.Width,
                    args.Height,
                    source.UprightWidth,
                    source.UprightHeight,
                    _options.CropFocus
                );

                var outputPixels = plan.MaxOutputPixels * source.Frames;

                if (outputPixels > _options.MaxPixels)
                {
                    _logger.LogImageTooLarge(outputPixels, _options.MaxPixels);

                    return ImageStreamResizeResult.Failed(
                        VipsImageSource.TooLargeError(outputPixels, _options.MaxPixels)
                    );
                }

                cancellationToken.ThrowIfCancellationRequested();

                using var resized = source.IsAnimation ? _ResizeFrames(source, plan) : _ResizeSingle(source, plan);
                var encoded = source.Format.Save(resized, _options);

                return ImageStreamResizeResult.Done(
                    new MemoryStream(encoded),
                    source.Format.MimeType,
                    resized.Width,
                    resized.PageHeight
                );
            }
        }
        catch (VipsException e)
        {
            _logger.LogEncodedImageInvalidContent(e);

            return ImageStreamResizeResult.NotSupported(VipsImageSource.InvalidContentError);
        }
    }

    private static Image _ResizeSingle(VipsImageSource source, VipsResizePlan plan)
    {
        // fail_on goes through the loader option string: thumbnail's own failOn argument does not reach the loader
        // (libvips 8.18), so a truncated JPEG or PNG would resize with its missing rows filled in.
        using var thumbnail = Image.ThumbnailBuffer(
            source.Bytes,
            plan.Width,
            optionString: "fail_on=error",
            height: plan.Height,
            size: plan.Size,
            crop: plan.Crop
        );

        return _Pad(thumbnail, plan, source.Format);
    }

    /// <summary>
    /// Resizes an animation frame by frame. libvips <c>thumbnail</c> scales a frame strip correctly but crops and pads
    /// it as one tall image, so each frame goes through the plan on its own and the strip is rebuilt.
    /// </summary>
    private static Image _ResizeFrames(VipsImageSource source, VipsResizePlan plan)
    {
        using var strip = source.Decode();
        var frameHeight = strip.PageHeight;
        var frames = new Image[strip.Height / frameHeight];

        try
        {
            for (var i = 0; i < frames.Length; i++)
            {
                using var frame = strip.ExtractArea(0, i * frameHeight, strip.Width, frameHeight);
                // A content-aware crop would pick a different window for each frame and make the animation shake, so
                // every frame is cut at the centre.
                using var thumbnail = frame.ThumbnailImage(
                    plan.Width,
                    height: plan.Height,
                    size: plan.Size,
                    crop: plan.Crop is null ? null : Enums.Interesting.Centre
                );

                frames[i] = _Pad(thumbnail, plan, source.Format);
            }

            // The joined strip takes its metadata (frame delays, loop count) from the first frame, so only the
            // frame height needs restating.
            using var joined = Image.Arrayjoin(frames, across: 1);
            var resizedFrameHeight = frames[0].Height;

            return joined.Mutate(image => image.Set(GValue.GIntType, "page-height", resizedFrameHeight));
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame?.Dispose();
            }
        }
    }

    /// <summary>
    /// Centers <paramref name="image" /> on the plan's canvas. The padding is transparent when the format stores
    /// alpha, and black otherwise.
    /// </summary>
    /// <returns>A new image the caller owns; <paramref name="image" /> stays owned by the caller.</returns>
    private static Image _Pad(Image image, VipsResizePlan plan, VipsFormat format)
    {
        if (!plan.Pads)
        {
            return image.Copy();
        }

        // thumbnail always hands back 8-bit sRGB, even from a 16-bit source, so 255 is the opaque alpha.
        var withAlpha = format.HasAlpha && !image.HasAlpha() ? image.Bandjoin(255) : image.Copy();

        using (withAlpha)
        {
            return withAlpha.Embed(
                (plan.CanvasWidth - withAlpha.Width) / 2,
                (plan.CanvasHeight - withAlpha.Height) / 2,
                plan.CanvasWidth,
                plan.CanvasHeight,
                extend: Enums.Extend.Background,
                background: [0]
            );
        }
    }
}
