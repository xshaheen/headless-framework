// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.Logging;
using NetVips;

namespace Headless.Imaging.Internal;

/// <summary>
/// An encoded image whose format passed the loader allowlist and whose header passed the pixel limit. Only the
/// header has been read; no pixel is decoded until a caller asks for one.
/// </summary>
internal sealed class VipsImageSource : IDisposable
{
    public const string UnknownFormatError = "The encoded image format is unknown or not supported.";
    public const string InvalidContentError = "The encoded image contains invalid content.";

    // libvips loads every frame of an animation as one tall strip only when asked; the default is the first frame.
    private const string _AllFramesOption = "n=-1";

    private readonly Image _header;

    private VipsImageSource(byte[] bytes, VipsFormat format, Image header, int frames)
    {
        Bytes = bytes;
        Format = format;
        _header = header;
        Frames = frames;
    }

    public byte[] Bytes { get; }

    public VipsFormat Format { get; }

    /// <summary>Gets the stored width of one frame, before EXIF orientation.</summary>
    public int Width => _header.Width;

    /// <summary>Gets the stored height of one frame, before EXIF orientation.</summary>
    public int Height => _header.Height;

    public int Frames { get; }

    /// <summary>Gets a value indicating whether the EXIF orientation turns the image a quarter, swapping its sides.</summary>
    public bool IsQuarterTurned => _header.Contains("orientation") && (int)_header.Get("orientation") is >= 5 and <= 8;

    public int UprightWidth => IsQuarterTurned ? Height : Width;

    public int UprightHeight => IsQuarterTurned ? Width : Height;

    public bool IsAnimation => Frames > 1;

    /// <summary>
    /// Reads the header of <paramref name="bytes" /> after its loader passes the allowlist, and checks the pixel limit.
    /// </summary>
    /// <returns>The source, or the error and state that explain the refusal.</returns>
    /// <exception cref="VipsException">The header is malformed.</exception>
    public static (VipsImageSource? Source, ImageProcessState State, string? Error) Open(
        byte[] bytes,
        NetVipsOptions options,
        ILogger logger
    )
    {
        // Sniffing runs only each loader's magic-number check, so a refused format never reaches its parser.
        var loader = bytes.Length == 0 ? null : Image.FindLoadBuffer(bytes);
        var format = loader is null ? null : VipsFormat.FromLoader(loader);

        if (format is null)
        {
            logger.LogImageFormatRefused(loader ?? "none");

            return (null, ImageProcessState.Unsupported, UnknownFormatError);
        }

        var header = Image.NewFromBuffer(bytes, failOn: Enums.FailOn.Error);

        try
        {
            if (format == VipsFormat.Avif && !_IsAv1(header))
            {
                logger.LogImageFormatRefused("heif-hevc");
                header.Dispose();

                return (null, ImageProcessState.Unsupported, UnknownFormatError);
            }

            var frames = format.IsAnimated && header.Contains("n-pages") ? Math.Max(1, (int)header.Get("n-pages")) : 1;
            var pixels = (long)header.Width * header.Height * frames;

            if (pixels > options.MaxPixels)
            {
                logger.LogImageTooLarge(pixels, options.MaxPixels);
                header.Dispose();

                return (null, ImageProcessState.Failed, TooLargeError(pixels, options.MaxPixels));
            }

            return (new VipsImageSource(bytes, format, header, frames), ImageProcessState.Done, null);
        }
        catch
        {
            header.Dispose();

            throw;
        }
    }

    public static string TooLargeError(long pixels, long maxPixels)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The image has {pixels} pixels, more than the configured maximum of {maxPixels}."
        );
    }

    /// <summary>Decodes the whole image: every frame of an animation, stacked as one strip.</summary>
    public Image Decode()
    {
        return Image.NewFromBuffer(Bytes, IsAnimation ? _AllFramesOption : "", failOn: Enums.FailOn.Error);
    }

    public void Dispose()
    {
        _header.Dispose();
    }

    private static bool _IsAv1(Image header)
    {
        return header.Contains("heif-compression")
            && string.Equals((string)header.Get("heif-compression"), "av1", StringComparison.Ordinal);
    }
}

internal static partial class VipsImageSourceLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ImageFormatRefused",
        Level = LogLevel.Information,
        Message = "The image format is unknown or outside the allowed set (loader: {Loader})"
    )]
    public static partial void LogImageFormatRefused(this ILogger logger, string loader);

    [LoggerMessage(
        EventId = 2,
        EventName = "EncodedImageInvalidContent",
        Level = LogLevel.Information,
        Message = "The encoded image contains invalid content"
    )]
    public static partial void LogEncodedImageInvalidContent(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "ImageTooLarge",
        Level = LogLevel.Information,
        Message = "The image has {Pixels} pixels, more than the configured maximum of {MaxPixels}"
    )]
    public static partial void LogImageTooLarge(this ILogger logger, long pixels, long maxPixels);
}
