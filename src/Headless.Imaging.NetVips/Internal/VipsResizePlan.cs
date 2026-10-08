// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NetVips;

namespace Headless.Imaging.Internal;

/// <summary>
/// How one <see cref="ImageResizeMode" /> maps onto a libvips <c>thumbnail</c> call and an optional <c>embed</c>.
/// </summary>
/// <param name="Width">The thumbnail target width.</param>
/// <param name="Height">The thumbnail target height.</param>
/// <param name="Size">Whether the thumbnail fits the box (<c>Both</c>) or lands on it exactly (<c>Force</c>).</param>
/// <param name="Crop">The crop strategy, or <see langword="null" /> to fit inside the box instead of filling it.</param>
/// <param name="CanvasWidth">The padded canvas width, or <c>0</c> when the mode does not pad.</param>
/// <param name="CanvasHeight">The padded canvas height, or <c>0</c> when the mode does not pad.</param>
internal sealed record VipsResizePlan(
    int Width,
    int Height,
    Enums.Size Size,
    Enums.Interesting? Crop,
    int CanvasWidth,
    int CanvasHeight
)
{
    public bool Pads => CanvasWidth > 0;

    /// <summary>Gets the largest pixel count the plan can produce per frame.</summary>
    public long MaxOutputPixels => Pads ? (long)CanvasWidth * CanvasHeight : (long)Width * Height;

    /// <summary>Plans the resize of an upright <paramref name="sourceWidth" /> × <paramref name="sourceHeight" /> image.</summary>
    /// <param name="mode">A resolved mode; never <see cref="ImageResizeMode.Default" /> or <see cref="ImageResizeMode.None" />.</param>
    /// <param name="width">The requested width, or <c>0</c> to derive it from <paramref name="height" />.</param>
    /// <param name="height">The requested height, or <c>0</c> to derive it from <paramref name="width" />.</param>
    /// <param name="sourceWidth">The source width after EXIF orientation.</param>
    /// <param name="sourceHeight">The source height after EXIF orientation.</param>
    /// <param name="focus">The region <see cref="ImageResizeMode.Crop" /> keeps.</param>
    /// <exception cref="InvalidOperationException"><paramref name="mode" /> is not a resizing mode.</exception>
    public static VipsResizePlan Create(
        ImageResizeMode mode,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight,
        NetVipsCropFocus focus
    )
    {
        // A missing side follows the source aspect ratio before the mode applies, so every mode works from a full
        // box. Without it, Crop and Pad would have no second side to fill.
        if (width == 0)
        {
            width = _Scale(sourceWidth, (double)height / sourceHeight);
        }

        if (height == 0)
        {
            height = _Scale(sourceHeight, (double)width / sourceWidth);
        }

        return mode switch
        {
            ImageResizeMode.Stretch => new(width, height, Enums.Size.Force, Crop: null, 0, 0),
            ImageResizeMode.Max => _Max(width, height, sourceWidth, sourceHeight),
            ImageResizeMode.Crop => new(width, height, Enums.Size.Both, _ToInteresting(focus), 0, 0),
            ImageResizeMode.Pad => _Pad(width, height),
            ImageResizeMode.BoxPad => sourceWidth <= width && sourceHeight <= height
                ? _Unscaled(sourceWidth, sourceHeight) with
                {
                    CanvasWidth = width,
                    CanvasHeight = height,
                }
                : _Pad(width, height),
            ImageResizeMode.Min => _Min(width, height, sourceWidth, sourceHeight),
            _ => throw new InvalidOperationException($"{nameof(ImageResizeMode)}.{mode} does not resize."),
        };
    }

    private static VipsResizePlan _Pad(int width, int height)
    {
        return new(width, height, Enums.Size.Both, Crop: null, width, height);
    }

    /// <summary>
    /// Fits inside the box without upscaling. The fitted size is computed here rather than left to libvips' shrink-only
    /// mode, so the output pixel limit sees the real output and not a box the source may never fill.
    /// </summary>
    private static VipsResizePlan _Max(int width, int height, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= width && sourceHeight <= height)
        {
            return _Unscaled(sourceWidth, sourceHeight);
        }

        var scale = Math.Min((double)width / sourceWidth, (double)height / sourceHeight);

        return new(_Scale(sourceWidth, scale), _Scale(sourceHeight, scale), Enums.Size.Force, Crop: null, 0, 0);
    }

    /// <summary>
    /// Puts the side whose target is nearest its source length exactly on the target and scales the other with the
    /// aspect ratio, which is the ImageSharp <c>ResizeMode.Min</c> rule the enum documents. A box larger than the
    /// source on either side would need an upscale, so the source keeps its size.
    /// </summary>
    private static VipsResizePlan _Min(int width, int height, int sourceWidth, int sourceHeight)
    {
        if (width > sourceWidth || height > sourceHeight)
        {
            return _Unscaled(sourceWidth, sourceHeight);
        }

        var widthGap = sourceWidth - width;
        var heightGap = sourceHeight - height;
        var keepWidth = widthGap < heightGap || (widthGap == heightGap && height > width);

        return keepWidth
            ? new(width, _Scale(sourceHeight, (double)width / sourceWidth), Enums.Size.Force, Crop: null, 0, 0)
            : new(_Scale(sourceWidth, (double)height / sourceHeight), height, Enums.Size.Force, Crop: null, 0, 0);
    }

    /// <summary>Re-encodes at the source size; <c>thumbnail</c> still applies the EXIF orientation.</summary>
    private static VipsResizePlan _Unscaled(int sourceWidth, int sourceHeight)
    {
        return new(sourceWidth, sourceHeight, Enums.Size.Force, Crop: null, 0, 0);
    }

    private static int _Scale(int length, double scale)
    {
        return Math.Max(1, (int)Math.Round(length * scale, MidpointRounding.AwayFromZero));
    }

    private static Enums.Interesting _ToInteresting(NetVipsCropFocus focus)
    {
        return focus switch
        {
            NetVipsCropFocus.Attention => Enums.Interesting.Attention,
            NetVipsCropFocus.Entropy => Enums.Interesting.Entropy,
            _ => Enums.Interesting.Centre,
        };
    }
}
