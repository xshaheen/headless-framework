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
/// The contributor reads only as much of a seekable stream as the header needs, asynchronously: a window that starts
/// at 64 KB and grows fourfold until libvips parses the header. A JPEG with large EXIF or ICC segments, a TIFF whose
/// directory sits at the end of the file (most do), or an AVIF whose metadata follows its pixel data takes a larger
/// window. A GIF or WebP is read whole, because counting its frames walks the entire file. A file no allowed loader
/// claims is refused from the first window.
/// </para>
/// <para>
/// The pixel limit is not applied: inspection only reports what the header declares, and reading a header is safe
/// whatever size it declares.
/// </para>
/// </remarks>
internal sealed class NetVipsImageInspectorContributor : IImageInspectorContributor
{
    private readonly ILogger<NetVipsImageInspectorContributor> _logger;

    public NetVipsImageInspectorContributor(ILogger<NetVipsImageInspectorContributor> logger)
    {
        VipsRuntime.EnsureAvailable();

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageInspectResult> TryInspectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            var (source, _, error) = await VipsImageSource
                .OpenAsync(stream, options: null, _logger, cancellationToken)
                .ConfigureAwait(false);

            if (source is null)
            {
                return ImageInspectResult.NotSupported(error!);
            }

            using (source)
            {
                return ImageInspectResult.Done(
                    new ImageInfo
                    {
                        MimeType = source.Format.MimeType,
                        Width = source.UprightWidth,
                        Height = source.UprightHeight,
                        FrameCount = source.Frames,
                        HasAlpha = source.HasAlpha,
                    }
                );
            }
        }
        catch (VipsException e)
        {
            _logger.LogEncodedImageInvalidContent(e);

            return ImageInspectResult.NotSupported(VipsImageSource.InvalidContentError);
        }
    }
}
