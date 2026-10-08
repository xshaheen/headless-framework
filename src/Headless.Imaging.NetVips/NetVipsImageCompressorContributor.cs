// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetVips;

namespace Headless.Imaging;

/// <summary>
/// An <see cref="IImageCompressorContributor" /> that re-encodes images with libvips at the quality
/// <see cref="NetVipsOptions" /> sets, in the source format or in <see cref="ImageCompressArgs.OutputMimeType" />.
/// </summary>
/// <remarks>
/// Compression succeeds only when the output is strictly smaller than the input; otherwise the contributor returns
/// <see cref="ImageProcessState.Failed" />. The output format must have a quality or compression setting to trade:
/// JPEG, PNG, WebP, or AVIF. Any allowed input converts to one of them, so a GIF can compress to WebP, but without an
/// output format a GIF or TIFF input yields <see cref="ImageProcessState.Unsupported" />.
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
        VipsFormat? requestedFormat = null;

        if (!string.IsNullOrWhiteSpace(args.OutputMimeType))
        {
            requestedFormat = VipsFormat.FromMimeType(args.OutputMimeType);

            if (requestedFormat is not { CanCompress: true })
            {
                return ImageStreamCompressResult.NotSupportedMimeType(args.OutputMimeType);
            }
        }

        if (
            !string.IsNullOrWhiteSpace(args.MimeType) && !_CanRead(args.MimeType, converts: requestedFormat is not null)
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
                var format = requestedFormat ?? source.Format;

                if (!format.CanCompress)
                {
                    return ImageStreamCompressResult.NotSupportedMimeType(source.Format.MimeType);
                }

                var keepsFrames = source.IsAnimation && format.IsAnimated;
                using var decoded = source.Decode(allFrames: keepsFrames);

                // Stripping metadata drops the EXIF orientation, so turn the pixels upright first. A frame strip is
                // left alone: rotating it would scramble the frames, and animations carry no orientation.
                using var upright = _options.StripMetadata && !keepsFrames ? decoded.Autorot() : decoded.Copy();
                var encoded = format.Save(upright, _options, cancellationToken);

                return encoded.Length < bytes.Length
                    ? ImageStreamCompressResult.Done(new MemoryStream(encoded))
                    : ImageStreamCompressResult.Failed(_LargerOutputError);
            }
        }
        catch (VipsException e) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogEncodedImageInvalidContent(e);

            return ImageStreamCompressResult.NotSupported(VipsImageSource.InvalidContentError);
        }
        catch (VipsException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled while libvips was reading the header or decoding: the error is a symptom of the abort, so
            // report the cancellation rather than invalid content.
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>
    /// Whether an input of <paramref name="mimeType" /> can be compressed: any allowed format when it converts to
    /// another one, and only a format with a quality setting when it keeps its own.
    /// </summary>
    private static bool _CanRead(string mimeType, bool converts)
    {
        var format = VipsFormat.FromMimeType(mimeType);

        return format is not null && (converts || format.CanCompress);
    }
}
