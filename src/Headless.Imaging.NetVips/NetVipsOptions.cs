// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Imaging;

/// <summary>Options that control how the libvips contributors decode, resize, and encode images.</summary>
/// <remarks>
/// The encoder settings apply to every image the contributors write, in the source format or in the requested
/// <c>OutputMimeType</c>, whether it comes from a resize or a compression.
/// </remarks>
[PublicAPI]
public sealed class NetVipsOptions
{
    /// <summary>
    /// The default for <see cref="MaxPixels" />: 16383 × 16383, the largest image a browser-facing pipeline normally
    /// meets, and the same ceiling the widely used sharp library applies.
    /// </summary>
    public const long DefaultMaxPixels = 16_383L * 16_383L;

    /// <summary>Gets or sets the JPEG quality factor, 1–100. Defaults to <c>75</c>.</summary>
    public int JpegQuality { get; set; } = 75;

    /// <summary>Gets or sets the lossy WebP quality factor, 1–100. Defaults to <c>75</c>.</summary>
    public int WebpQuality { get; set; } = 75;

    /// <summary>Gets or sets the AVIF (AV1) quality factor, 1–100. Defaults to <c>50</c>, libvips' own default.</summary>
    public int AvifQuality { get; set; } = 50;

    /// <summary>
    /// Gets or sets the zlib compression level for PNG output, 0 (fastest, largest) to 9 (slowest, smallest). PNG is
    /// lossless at every level. Defaults to <c>9</c>.
    /// </summary>
    public int PngCompressionLevel { get; set; } = 9;

    /// <summary>
    /// Gets or sets a value indicating whether JPEG output is progressive, so a browser draws a coarse full image first
    /// and refines it. Defaults to <see langword="false" />, baseline JPEG.
    /// </summary>
    public bool JpegProgressive { get; set; }

    /// <summary>
    /// Gets or sets whether JPEG and AVIF output store colour at half resolution. Defaults to
    /// <see cref="NetVipsChromaSubsampling.Auto" />.
    /// </summary>
    public NetVipsChromaSubsampling ChromaSubsampling { get; set; } = NetVipsChromaSubsampling.Auto;

    /// <summary>
    /// Gets or sets a value indicating whether output images drop EXIF, XMP, IPTC, and other metadata. Defaults to
    /// <see langword="true" />, so camera details and GPS coordinates never leak into a published image.
    /// </summary>
    /// <remarks>
    /// The ICC colour profile is always kept, because dropping it shifts the colours of wide-gamut images. When
    /// metadata is stripped, the compressor first rotates the pixels to match the EXIF orientation, which would
    /// otherwise be lost with the EXIF block. The resizer always applies the orientation.
    /// </remarks>
    public bool StripMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest pixel count, width × height × frames, of an input image or a resize output. Defaults to
    /// <see cref="DefaultMaxPixels" />.
    /// </summary>
    /// <remarks>
    /// The input check reads only the image header, so a decompression bomb (a small file that declares an enormous
    /// image) is refused before any pixel is decoded. The output check stops a resize request from allocating an
    /// enormous canvas. Either violation yields <see cref="ImageProcessState.Failed" />, not
    /// <see cref="ImageProcessState.Unsupported" />, so the pipeline does not hand the image to another contributor.
    /// </remarks>
    public long MaxPixels { get; set; } = DefaultMaxPixels;

    /// <summary>
    /// Gets or sets which part of the image <see cref="ImageResizeMode.Crop" /> keeps. Defaults to
    /// <see cref="NetVipsCropFocus.Center" />.
    /// </summary>
    /// <remarks>
    /// Animations are always cropped at the center: a content-aware focus would pick a different window for each frame
    /// and make the animation shake.
    /// </remarks>
    public NetVipsCropFocus CropFocus { get; set; } = NetVipsCropFocus.Center;
}

internal sealed class NetVipsOptionsValidator : AbstractValidator<NetVipsOptions>
{
    public NetVipsOptionsValidator()
    {
        RuleFor(x => x.JpegQuality).InclusiveBetween(1, 100);
        RuleFor(x => x.WebpQuality).InclusiveBetween(1, 100);
        RuleFor(x => x.AvifQuality).InclusiveBetween(1, 100);
        RuleFor(x => x.PngCompressionLevel).InclusiveBetween(0, 9);
        RuleFor(x => x.MaxPixels).GreaterThan(0);
        RuleFor(x => x.CropFocus).IsInEnum();
        RuleFor(x => x.ChromaSubsampling).IsInEnum();
    }
}
