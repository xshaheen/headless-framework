// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetVips;

namespace Headless.Imaging;

/// <summary>
/// An <see cref="IImageCompressorContributor" /> that re-encodes JPEG, PNG, WebP, and AVIF images with libvips, in the
/// source format, at the quality <see cref="NetVipsOptions" /> sets.
/// </summary>
/// <remarks>
/// Compression succeeds only when the output is strictly smaller than the input; otherwise the contributor returns
/// <see cref="ImageProcessState.Failed" />. GIF and TIFF decode but have no quality setting to trade, so they yield
/// <see cref="ImageProcessState.Unsupported" />, like every format outside the allowlist.
/// </remarks>
internal sealed class NetVipsImageCompressorContributor : IImageCompressorContributor
{
    private const string _LargerOutputError = "The compressed image is larger than the original.";

    private readonly NetVipsOptions _options;
    private readonly ILogger<NetVipsImageCompressorContributor> _logger;

    public NetVipsImageCompressorContributor(
        IOptions<NetVipsOptions> optionsAccessor,
        ILogger<NetVipsImageCompressorContributor> logger
    )
    {
        VipsRuntime.EnsureAvailable();

        _options = optionsAccessor.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageStreamCompressResult> TryCompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !string.IsNullOrWhiteSpace(args.MimeType)
            && VipsFormat.FromMimeType(args.MimeType) is not { CanCompress: true }
        )
        {
            return ImageStreamCompressResult.NotSupportedMimeType(args.MimeType);
        }

        var bytes = await stream.GetAllBytesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var (source, state, error) = VipsImageSource.Open(bytes, _options, _logger);

            if (source is null)
            {
                return state is ImageProcessState.Failed
                    ? ImageStreamCompressResult.Failed(error!)
                    : ImageStreamCompressResult.NotSupported(error!);
            }

            using (source)
            {
                if (!source.Format.CanCompress)
                {
                    return ImageStreamCompressResult.NotSupportedMimeType(source.Format.MimeType);
                }

                cancellationToken.ThrowIfCancellationRequested();

                using var decoded = source.Decode();

                // Stripping metadata drops the EXIF orientation, so turn the pixels upright first. An animation is
                // left alone: rotating its frame strip would scramble the frames, and animations carry no orientation.
                using var upright = _options.StripMetadata && !source.IsAnimation ? decoded.Autorot() : decoded.Copy();
                var encoded = source.Format.Save(upright, _options);

                return encoded.Length < bytes.Length
                    ? ImageStreamCompressResult.Done(new MemoryStream(encoded))
                    : ImageStreamCompressResult.Failed(_LargerOutputError);
            }
        }
        catch (VipsException e)
        {
            _logger.LogEncodedImageInvalidContent(e);

            return ImageStreamCompressResult.NotSupported(VipsImageSource.InvalidContentError);
        }
    }
}
