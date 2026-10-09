// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetVips;

namespace Headless.Imaging;

/// <summary>
/// An <see cref="IImageResizerContributor" /> that resizes JPEG, PNG, WebP, GIF, TIFF, and AVIF images with libvips,
/// and converts between those formats.
/// </summary>
/// <remarks>
/// <para>
/// The output takes <see cref="ImageResizeArgs.OutputMimeType" /> when set, and otherwise the format detected from the
/// bytes: the <see cref="ImageResizeArgs.MimeType" /> hint only lets the contributor skip a type it does not handle. The
/// resize runs through libvips <c>thumbnail</c>, which shrinks JPEG and WebP while decoding and applies the EXIF
/// orientation, so the output is upright and its width and height are the displayed ones.
/// </para>
/// <para>
/// When <see cref="ImageResizeArgs.Mode" /> resolves to <see cref="ImageResizeMode.None" /> and no other output format
/// is requested, the caller's stream comes back unchanged, rewound to its start, with the displayed width and height.
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

        var requestedFormat = string.IsNullOrWhiteSpace(args.OutputMimeType)
            ? null
            : VipsFormat.FromMimeType(args.OutputMimeType);

        if (requestedFormat is null && !string.IsNullOrWhiteSpace(args.OutputMimeType))
        {
            return ImageStreamResizeResult.NotSupportedMimeType(args.OutputMimeType);
        }

        try
        {
            var (source, state, error) = await VipsImageSource
                .OpenAsync(stream, _options, _logger, cancellationToken)
                .ConfigureAwait(false);

            if (source is null)
            {
                return state is ImageProcessState.Failed
                    ? ImageStreamResizeResult.Failed(error!)
                    : ImageStreamResizeResult.NotSupported(error!);
            }

            using (source)
            {
                var format = requestedFormat ?? source.Format;
                var resizes = args.Mode is not (ImageResizeMode.None or ImageResizeMode.Default);

                if (!resizes && format == source.Format)
                {
                    return ImageStreamResizeResult.Done(
                        source.OpenOriginal(),
                        format.MimeType,
                        source.UprightWidth,
                        source.UprightHeight
                    );
                }

                // A conversion without a resize re-encodes at the source size; thumbnail still turns it upright.
                var plan = resizes
                    ? VipsResizePlan.Create(
                        args.Mode,
                        args.Width,
                        args.Height,
                        source.UprightWidth,
                        source.UprightHeight,
                        _options.CropFocus
                    )
                    : VipsResizePlan.Unscaled(source.UprightWidth, source.UprightHeight);

                // An animation keeps its frames only in a format that can hold them; otherwise the first frame stands
                // for the whole image.
                var keepsFrames = source.IsAnimation && format.IsAnimated;
                var outputPixels = plan.MaxOutputPixels * (keepsFrames ? source.Frames : 1);

                if (outputPixels > _options.MaxPixels)
                {
                    _logger.LogImageTooLarge(outputPixels, _options.MaxPixels);

                    return ImageStreamResizeResult.Failed(
                        VipsImageSource.TooLargeError(outputPixels, _options.MaxPixels)
                    );
                }

                await source.LoadContentAsync(cancellationToken).ConfigureAwait(false);

                using var resized = keepsFrames ? _ResizeFrames(source, plan) : _ResizeSingle(source, plan);
                var encoded = format.Save(resized, _options, cancellationToken);

                return ImageStreamResizeResult.Done(
                    new MemoryStream(encoded),
                    format.MimeType,
                    resized.Width,
                    resized.PageHeight
                );
            }
        }
        catch (VipsException e) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogEncodedImageInvalidContent(e);

            return ImageStreamResizeResult.NotSupported(VipsImageSource.InvalidContentError);
        }
        catch (VipsException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled while libvips was reading the header or decoding: the error is a symptom of the abort, so
            // report the cancellation rather than invalid content.
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static Image _ResizeSingle(VipsImageSource source, VipsResizePlan plan)
    {
        using var thumbnail = source.Thumbnail(plan);

        return _Pad(thumbnail, plan);
    }

    /// <summary>
    /// Resizes an animation frame by frame. libvips <c>thumbnail</c> scales a frame strip correctly but crops and pads
    /// it as one tall image, so each frame goes through the plan on its own and the strip is rebuilt.
    /// </summary>
    private static Image _ResizeFrames(VipsImageSource source, VipsResizePlan plan)
    {
        using var strip = source.Decode(allFrames: true);
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

                frames[i] = _Pad(thumbnail, plan);
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
    /// Centers <paramref name="image" /> on the plan's transparent canvas. Saving to a format without alpha flattens the
    /// padding onto white, the same as any other transparent area.
    /// </summary>
    /// <returns>A new image the caller owns; <paramref name="image" /> stays owned by the caller.</returns>
    private static Image _Pad(Image image, VipsResizePlan plan)
    {
        if (!plan.Pads)
        {
            return image.Copy();
        }

        // thumbnail always hands back 8-bit sRGB, even from a 16-bit source, so 255 is the opaque alpha.
        using var withAlpha = image.HasAlpha() ? image.Copy() : image.Bandjoin(255);

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
