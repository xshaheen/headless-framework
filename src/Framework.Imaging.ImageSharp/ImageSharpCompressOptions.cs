using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Framework.Imaging.ImageSharp;

public sealed class ImageSharpCompressOptions
{
    public IImageEncoder JpegEncoder { get; set; }

    public IImageEncoder PngEncoder { get; set; }

    public IImageEncoder WebpEncoder { get; set; }

    public int DefaultQuality { get; set; } = 75;

    public ImageSharpCompressOptions()
    {
        JpegEncoder = new JpegEncoder { Quality = DefaultQuality };
        PngEncoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression, SkipMetadata = true };
        WebpEncoder = new WebpEncoder { Quality = DefaultQuality };
    }
}
