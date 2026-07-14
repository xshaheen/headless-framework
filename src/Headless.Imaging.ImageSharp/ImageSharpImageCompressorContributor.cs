// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Constants;
using Headless.Imaging.ImageSharp.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Formats;
using Image = SixLabors.ImageSharp.Image;

namespace Headless.Imaging.ImageSharp;

/// <summary>
/// An <see cref="IImageCompressorContributor"/> that uses ImageSharp to compress JPEG, PNG, and WebP images.
/// </summary>
/// <remarks>
/// Compression is considered successful only when the re-encoded output is strictly smaller than the
/// original stream. If the output is equal to or larger than the original, the contributor returns
/// <see cref="ImageProcessState.Failed"/> and disposes the intermediate stream. GIF, BMP, TIFF, and
/// other formats are not supported for compression and yield <see cref="ImageProcessState.Unsupported"/>.
/// </remarks>
internal sealed class ImageSharpImageCompressorContributor(
    IOptions<ImageSharpOptions> optionsAccessor,
    ILogger<ImageSharpImageCompressorContributor> logger
) : IImageCompressorContributor
{
    private readonly ImageSharpOptions _options = optionsAccessor.Value;

    /// <inheritdoc />
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

        var (image, error) = await LoadImageHelpers.TryLoad(stream, logger, cancellationToken).ConfigureAwait(false);

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

        var memoryStream = await _CreateCompressedStreamAsync(image, format, cancellationToken).ConfigureAwait(false);

        if (memoryStream.Length < stream.Length)
        {
            return ImageStreamCompressResult.Done(memoryStream);
        }

        await memoryStream.DisposeAsync().ConfigureAwait(false);

        return ImageStreamCompressResult.Failed("The compressed image is larger than the original.");
    }

    private async Task<Stream> _CreateCompressedStreamAsync(Image image, IImageFormat format, CancellationToken token)
    {
        var memoryStream = new MemoryStream();

        try
        {
            await image
                .SaveAsync(memoryStream, _GetCompressEncoder(format), cancellationToken: token)
                .ConfigureAwait(false);

            memoryStream.Position = 0;

            return memoryStream;
        }
        catch
        {
            await memoryStream.DisposeAsync().ConfigureAwait(false);

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
