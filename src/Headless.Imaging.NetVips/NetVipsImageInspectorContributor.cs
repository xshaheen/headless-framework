// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging.Internal;
using Microsoft.Extensions.Logging;
using NetVips;

namespace Headless.Imaging;

/// <summary>
/// An <see cref="IImageInspectorContributor" /> that reads JPEG, PNG, WebP, GIF, TIFF, and AVIF headers with libvips,
/// the same formats, behind the same allowlist, that the NetVips resizer and compressor process.
/// </summary>
/// <remarks>
/// <para>
/// The contributor reads only as much of a seekable stream as the header needs. It starts with the first 64 KB and
/// grows the window fourfold until libvips parses the header: a JPEG with large EXIF or ICC segments, a TIFF whose
/// directory sits at the end of the file (most do), or an AVIF whose metadata follows its pixel data takes a larger
/// window. A GIF or WebP is read whole, because counting its frames walks the entire file. A file that is none of the
/// allowed formats is refused from the first window. All reads are asynchronous.
/// </para>
/// <para>
/// The pixel limit is not applied: inspection only reports what the header declares, and reading a header is safe
/// whatever size it declares.
/// </para>
/// </remarks>
internal sealed class NetVipsImageInspectorContributor : IImageInspectorContributor
{
    private const int _InitialWindow = 64 * 1024;
    private const int _WindowGrowth = 4;

    private readonly ILogger<NetVipsImageInspectorContributor> _logger;

    public NetVipsImageInspectorContributor(ILogger<NetVipsImageInspectorContributor> logger)
    {
        VipsRuntime.EnsureAvailable();

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageInspectResult> TryInspectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanSeek)
        {
            var bytes = await stream.GetAllBytesAsync(cancellationToken).ConfigureAwait(false);

            return _Inspect(bytes, isWhole: true).Result ?? ImageInspectResult.NotSupported();
        }

        // The image starts at the beginning of the stream, as for the resizer and compressor, wherever the caller
        // left the position.
        const long start = 0;
        var remaining = stream.Length;
        var window = (int)Math.Min(remaining, _InitialWindow);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stream.Position = start;
            var prefix = new byte[window];
            await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);

            var isWhole = window == remaining;
            var (result, needsWhole) = _Inspect(prefix, isWhole);

            if (result is not null)
            {
                return result;
            }

            // An image too large for one array cannot be inspected; the resizer could not read it either.
            if (window == Array.MaxLength)
            {
                return ImageInspectResult.NotSupported(VipsImageSource.InvalidContentError);
            }

            var grown = needsWhole ? remaining : (long)window * _WindowGrowth;
            window = (int)Math.Min(Math.Min(remaining, grown), Array.MaxLength);
        }
    }

    /// <summary>Inspects <paramref name="bytes" />, a prefix of the image or the whole of it.</summary>
    /// <returns>
    /// The result; or no result when a prefix was too short, with <c>NeedsWhole</c> set when only the whole file will
    /// do.
    /// </returns>
    private (ImageInspectResult? Result, bool NeedsWhole) _Inspect(byte[] bytes, bool isWhole)
    {
        try
        {
            using var source = VipsImageSource.ReadHeader(bytes, _logger);

            if (source is null)
            {
                // libtiff recognises a TIFF only by opening its image directory, which usually follows the pixel data,
                // so a TIFF prefix sniffs as unknown. Every other allowed format announces itself in its first bytes,
                // so anything else is refused without reading further.
                return !isWhole && VipsFormat.HasTiffSignature(bytes)
                    ? (null, false)
                    : (ImageInspectResult.NotSupported(VipsImageSource.UnknownFormatError), false);
            }

            if (source.Format.IsAnimated && !isWhole)
            {
                return (null, true);
            }

            var info = new ImageInfo
            {
                MimeType = source.Format.MimeType,
                Width = source.UprightWidth,
                Height = source.UprightHeight,
                FrameCount = source.Frames,
                HasAlpha = source.HasAlpha,
            };

            return (ImageInspectResult.Done(info), false);
        }
        catch (VipsException e)
        {
            if (!isWhole)
            {
                return (null, false);
            }

            _logger.LogEncodedImageInvalidContent(e);

            return (ImageInspectResult.NotSupported(VipsImageSource.InvalidContentError), false);
        }
    }
}
