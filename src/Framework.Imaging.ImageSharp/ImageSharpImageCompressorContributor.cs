// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Imaging.Contracts;
using Framework.Imaging.ImageSharp.Internals;
using Framework.Kernel.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Formats;
using Image = SixLabors.ImageSharp.Image;

namespace Framework.Imaging.ImageSharp;

public sealed class ImageSharpImageCompressorContributor(
    IOptions<ImageSharpOptions> options,
    ILogger<ImageSharpImageCompressorContributor> logger
) : IImageCompressorContributor
{
    private readonly ImageSharpOptions _options = options.Value;

    public async Task<ImageStreamCompressResult> TryCompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(args.MimeType) && !_CanCompress(args.MimeType))
        {
            return ImageStreamCompressResult.NotSupported();
        }

        var (image, error) = await LoadImageHelpers.TryLoad(stream, logger, cancellationToken);

        if (error is not null)
        {
            return ImageStreamCompressResult.NotSupported(error);
        }

        Debug.Assert(image is not null);
        var format = image.Metadata.DecodedImageFormat;

        if (format is null)
        {
            return ImageStreamCompressResult.NotSupported();
        }

        if (!_CanCompress(format.DefaultMimeType))
        {
            return ImageStreamCompressResult.NotSupportedMimeType(format.DefaultMimeType);
        }

        var memoryStream = await _CreateCompressedStreamAsync(image, format, cancellationToken);

        if (memoryStream.Length < stream.Length)
        {
            return ImageStreamCompressResult.Done(memoryStream);
        }

        await memoryStream.DisposeAsync();

        return ImageStreamCompressResult.Failed("The compressed image is larger than the original.");
    }

    private async Task<Stream> _CreateCompressedStreamAsync(Image image, IImageFormat format, CancellationToken token)
    {
        var memoryStream = new MemoryStream();

        try
        {
            await image.SaveAsync(memoryStream, _GetCompressEncoder(format), cancellationToken: token);

            memoryStream.Position = 0;

            return memoryStream;
        }
        catch
        {
            await memoryStream.DisposeAsync();

            throw;
        }
    }

    private IImageEncoder _GetCompressEncoder(IImageFormat format)
    {
        return format.DefaultMimeType switch
        {
            ContentTypes.Images.Jpeg => _options.JpegCompressEncoder,
            ContentTypes.Images.Png => _options.PngCompressEncoder,
            ContentTypes.Images.Webp => _options.WebpCompressEncoder,
            _ => throw new NotSupportedException($"No encoder available for the given format: {format.Name}"),
        };
    }

    private static bool _CanCompress(string? mimeType)
    {
        return mimeType switch
        {
            ContentTypes.Images.Jpeg => true,
            ContentTypes.Images.Png => true,
            ContentTypes.Images.Webp => true,
            _ => false,
        };
    }
}
