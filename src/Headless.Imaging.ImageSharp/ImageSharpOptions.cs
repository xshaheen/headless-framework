// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Headless.Imaging.ImageSharp;

/// <summary>Options that control the ImageSharp-based image compressor contributor.</summary>
public sealed class ImageSharpOptions
{
    /// <summary>
    /// Gets or sets the default quality level used to initialize the built-in JPEG and WebP encoders.
    /// Valid range is 1-100. Defaults to <c>75</c>.
    /// </summary>
    /// <remarks>
    /// Changing this property after construction does not update the already-created encoder instances
    /// in <see cref="WebpCompressEncoder"/> or <see cref="JpegCompressEncoder"/>. Replace those
    /// properties explicitly when a non-default quality is required.
    /// </remarks>
    public int DefaultCompressQuality { get; set; } = 75;

    /// <summary>
    /// Gets or sets the ImageSharp encoder used when compressing WebP images.
    /// Defaults to <c>WebpEncoder</c> with quality set to <see cref="DefaultCompressQuality"/>.
    /// </summary>
    public IImageEncoder WebpCompressEncoder { get; set; }

    /// <summary>
    /// Gets or sets the ImageSharp encoder used when compressing JPEG images.
    /// Defaults to <c>JpegEncoder</c> with quality set to <see cref="DefaultCompressQuality"/>.
    /// </summary>
    public IImageEncoder JpegCompressEncoder { get; set; }

    /// <summary>
    /// Gets or sets the ImageSharp encoder used when compressing PNG images.
    /// Defaults to <c>PngEncoder</c> with <c>BestCompression</c> and metadata stripping enabled.
    /// </summary>
    public IImageEncoder PngCompressEncoder { get; set; }

    /// <summary>Initializes a new instance with default encoder settings.</summary>
    public ImageSharpOptions()
    {
        WebpCompressEncoder = new WebpEncoder { Quality = DefaultCompressQuality };
        JpegCompressEncoder = new JpegEncoder { Quality = DefaultCompressQuality };

        PngCompressEncoder = new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.BestCompression,
            SkipMetadata = true,
        };
    }
}

internal sealed class ImageSharpOptionsValidator : AbstractValidator<ImageSharpOptions>
{
    public ImageSharpOptionsValidator()
    {
        RuleFor(x => x.DefaultCompressQuality).InclusiveBetween(1, 100);
        RuleFor(x => x.WebpCompressEncoder).NotNull();
        RuleFor(x => x.JpegCompressEncoder).NotNull();
        RuleFor(x => x.PngCompressEncoder).NotNull();
    }
}
