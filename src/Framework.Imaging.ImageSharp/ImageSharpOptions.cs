using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Framework.Imaging.ImageSharp;

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
