// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Headless.Imaging.ImageSharp;

public sealed class ImageSharpOptions
{
    public int DefaultCompressQuality { get; set; } = 75;

    public IImageEncoder WebpCompressEncoder { get; set; }

    public IImageEncoder JpegCompressEncoder { get; set; }

    public IImageEncoder PngCompressEncoder { get; set; }

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

public sealed class ImageSharpOptionsValidator : AbstractValidator<ImageSharpOptions>
{
    public ImageSharpOptionsValidator()
    {
        RuleFor(x => x.DefaultCompressQuality).InclusiveBetween(1, 100);
        RuleFor(x => x.WebpCompressEncoder).NotNull();
        RuleFor(x => x.JpegCompressEncoder).NotNull();
        RuleFor(x => x.PngCompressEncoder).NotNull();
    }
}
