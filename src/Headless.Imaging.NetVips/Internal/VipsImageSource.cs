// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.Logging;
using NetVips;

namespace Headless.Imaging.Internal;

/// <summary>
/// An encoded image whose format passed the loader allowlist, opened by reading only its header. The pixels are read
/// later, by libvips, straight from the input wherever that is safe.
/// </summary>
/// <remarks>
/// <para>
/// The header is read asynchronously in a window that starts at 64 KB and grows fourfold until libvips parses it, so
/// a refused format, an image over the pixel limit, or a pass-through costs a few kilobytes, not the whole upload. A
/// GIF or WebP is read whole, because counting its frames walks the file.
/// </para>
/// <para>
/// libvips reads its input synchronously, from its own worker threads. A <see cref="MemoryStream" /> or
/// <see cref="FileStream" /> is safe to read that way, so libvips streams from it directly and the upload is never
/// copied. Any other stream gets one asynchronous copy into an exact-size buffer first: ASP.NET Core, for one, throws on
/// a synchronous read from a request body that is not fully buffered yet.
/// </para>
/// </remarks>
internal sealed class VipsImageSource : IDisposable
{
    public const string UnknownFormatError = "The encoded image format is unknown or not supported.";
    public const string InvalidContentError = "The encoded image contains invalid content.";

    // libvips loads every frame of an animation as one tall strip only when asked; the default is the first frame.
    private const string _AllFramesOption = "n=-1";

    // fail_on goes through the loader option string: thumbnail's own failOn argument does not reach the loader
    // (libvips 8.18), so a truncated JPEG or PNG would resize with its missing rows filled in.
    private const string _FailOnErrorOption = "fail_on=error";

    private const int _InitialWindow = 64 * 1024;
    private const int _WindowGrowth = 4;

#pragma warning disable CA2213 // False positive: the caller owns the input stream; the source only reads it.
    private readonly Stream _input;
#pragma warning restore CA2213
    private readonly byte[]? _wholeInput;
    private readonly List<DetachableReadStream> _readers = [];
    private Stream? _content;
    private bool _ownsContent;

    private VipsImageSource(Stream input, byte[]? wholeInput, long length, VipsHeader header)
    {
        _input = input;
        _wholeInput = wholeInput;
        Length = length;
        Format = header.Format;
        Width = header.Width;
        Height = header.Height;
        Frames = header.Frames;
        HasAlpha = header.HasAlpha;
        IsQuarterTurned = header.IsQuarterTurned;
    }

    public VipsFormat Format { get; }

    /// <summary>Gets the size of the encoded input in bytes.</summary>
    public long Length { get; }

    /// <summary>Gets the stored width of one frame, before EXIF orientation.</summary>
    public int Width { get; }

    /// <summary>Gets the stored height of one frame, before EXIF orientation.</summary>
    public int Height { get; }

    public int Frames { get; }

    /// <summary>Gets a value indicating whether the image has an alpha channel, read from the header.</summary>
    public bool HasAlpha { get; }

    /// <summary>Gets a value indicating whether the EXIF orientation turns the image a quarter, swapping its sides.</summary>
    public bool IsQuarterTurned { get; }

    public int UprightWidth => IsQuarterTurned ? Height : Width;

    public int UprightHeight => IsQuarterTurned ? Width : Height;

    public bool IsAnimation => Frames > 1;

    /// <summary>
    /// Reads the header of the image at the start of <paramref name="stream" />, refusing a format outside the
    /// allowlist and, when <paramref name="options" /> is given, an image over its pixel limit.
    /// </summary>
    /// <returns>The source, or the state and error that explain the refusal.</returns>
    /// <exception cref="VipsException">The whole input was read and its header is still malformed.</exception>
    public static async Task<(VipsImageSource? Source, ImageProcessState State, string? Error)> OpenAsync(
        Stream stream,
        NetVipsOptions? options,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        // A stream that cannot seek is read whole once; the header and the pixels then come from that copy.
        if (!stream.CanSeek)
        {
            var whole = await stream.GetAllBytesAsync(cancellationToken).ConfigureAwait(false);
            var outcome = _ReadHeader(whole, isWhole: true, logger);

            return _Finish(stream, whole, whole.Length, outcome, options, logger);
        }

        // The image starts at the beginning of the stream wherever the caller left the position, as the pipeline
        // rewinds it for every contributor.
        var length = stream.Length;
        var window = (int)Math.Min(length, _InitialWindow);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stream.Position = 0;
            var prefix = new byte[window];
            await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);

            var isWhole = window == length;
            var outcome = _ReadHeader(prefix, isWhole, logger);

            if (outcome.Header is not null || outcome.Refusal is not null)
            {
                return _Finish(stream, isWhole ? prefix : null, length, outcome, options, logger);
            }

            // An input too large for one array cannot be read whole, so its header can grow no further.
            if (window == Array.MaxLength)
            {
                return (null, ImageProcessState.Unsupported, InvalidContentError);
            }

            var grown = outcome.NeedsWhole ? length : (long)window * _WindowGrowth;
            window = (int)Math.Min(Math.Min(length, grown), Array.MaxLength);
        }
    }

    public static string TooLargeError(long pixels, long maxPixels)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The image has {pixels} pixels, more than the configured maximum of {maxPixels}."
        );
    }

    /// <summary>
    /// Opens a stream over the original bytes for a pass-through: the caller's own stream when it can be rewound, and
    /// otherwise a new one over the copy the header read made.
    /// </summary>
    public Stream OpenOriginal()
    {
        if (!_input.CanSeek)
        {
            return new MemoryStream(_wholeInput!, writable: false);
        }

        _input.Position = 0;

        return _input;
    }

    /// <summary>
    /// Makes the pixels readable: the caller's stream itself when libvips can read it synchronously, and otherwise one
    /// asynchronous copy. Call once, before <see cref="Thumbnail" /> or <see cref="Decode" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">The input is larger than one array can hold.</exception>
    public async Task LoadContentAsync(CancellationToken cancellationToken)
    {
        if (_wholeInput is not null)
        {
            _content = new MemoryStream(_wholeInput, writable: false);
            _ownsContent = true;

            return;
        }

        if (_input is MemoryStream or FileStream)
        {
            _content = _input;

            return;
        }

        if (Length > Array.MaxLength)
        {
            throw new InvalidOperationException("The encoded image is too large to buffer.");
        }

        var buffer = new byte[Length];
        _input.Position = 0;
        await _input.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        _content = new MemoryStream(buffer, writable: false);
        _ownsContent = true;
    }

    /// <summary>Runs libvips <c>thumbnail</c> on the first frame, shrinking JPEG and WebP while decoding.</summary>
    public Image Thumbnail(VipsResizePlan plan)
    {
        return Image.ThumbnailStream(
            _NewReader(),
            plan.Width,
            optionString: _FailOnErrorOption,
            height: plan.Height,
            size: plan.Size,
            crop: plan.Crop
        );
    }

    /// <summary>
    /// Decodes the image; with <paramref name="allFrames" />, every frame of an animation stacked as one strip, and
    /// otherwise the first frame.
    /// </summary>
    public Image Decode(bool allFrames)
    {
        return Image.NewFromStream(
            _NewReader(),
            allFrames && IsAnimation ? _AllFramesOption : "",
            failOn: Enums.FailOn.Error
        );
    }

    public void Dispose()
    {
        foreach (var reader in _readers)
        {
            reader.Detach();
        }

        if (_ownsContent)
        {
            _content?.Dispose();
        }
    }

    private DetachableReadStream _NewReader()
    {
        var content = _content ?? throw new InvalidOperationException("Call LoadContentAsync before reading pixels.");
        content.Position = 0;

        var reader = new DetachableReadStream(content);
        _readers.Add(reader);

        return reader;
    }

    private static (VipsImageSource? Source, ImageProcessState State, string? Error) _Finish(
        Stream stream,
        byte[]? wholeInput,
        long length,
        HeaderOutcome outcome,
        NetVipsOptions? options,
        ILogger logger
    )
    {
        if (outcome.Header is not { } header)
        {
            return (null, ImageProcessState.Unsupported, outcome.Refusal ?? UnknownFormatError);
        }

        var pixels = (long)header.Width * header.Height * header.Frames;

        if (options is not null && pixels > options.MaxPixels)
        {
            logger.LogImageTooLarge(pixels, options.MaxPixels);

            return (null, ImageProcessState.Failed, TooLargeError(pixels, options.MaxPixels));
        }

        return (new VipsImageSource(stream, wholeInput, length, header), ImageProcessState.Done, null);
    }

    /// <summary>Reads the header from <paramref name="bytes" />, a prefix of the input or the whole of it.</summary>
    /// <exception cref="VipsException">The whole input was read and its header is still malformed.</exception>
    private static HeaderOutcome _ReadHeader(byte[] bytes, bool isWhole, ILogger logger)
    {
        // Sniffing runs only each loader's magic-number check, so a refused format never reaches its parser.
        var loader = bytes.Length == 0 ? null : Image.FindLoadBuffer(bytes);
        var format = loader is null ? null : VipsFormat.FromLoader(loader);

        if (format is null)
        {
            // libtiff recognises a TIFF only by opening its image directory, which usually follows the pixel data, so
            // a TIFF prefix sniffs as unknown. Every other allowed format announces itself in its first bytes, so
            // anything else is refused without reading further.
            if (!isWhole && VipsFormat.HasTiffSignature(bytes))
            {
                return HeaderOutcome.NeedsMore;
            }

            logger.LogImageFormatRefused(loader ?? "none");

            return HeaderOutcome.Refused(UnknownFormatError);
        }

        if (format.IsAnimated && !isWhole)
        {
            return new HeaderOutcome(Header: null, Refusal: null, NeedsWhole: true);
        }

        using var memory = new MemoryStream(bytes, writable: false);
        using var reader = new DetachableReadStream(memory);

        try
        {
            using var image = Image.NewFromStream(reader, failOn: Enums.FailOn.Error);

            if (format == VipsFormat.Avif && !_IsAv1(image))
            {
                logger.LogImageFormatRefused("heif-hevc");

                return HeaderOutcome.Refused(UnknownFormatError);
            }

            var header = new VipsHeader(
                format,
                image.Width,
                image.Height,
                format.IsAnimated && image.Contains("n-pages") ? Math.Max(1, (int)image.Get("n-pages")) : 1,
                image.HasAlpha(),
                image.Contains("orientation") && (int)image.Get("orientation") is >= 5 and <= 8
            );

            return new HeaderOutcome(header, Refusal: null, NeedsWhole: false);
        }
        catch (VipsException) when (!isWhole)
        {
            return HeaderOutcome.NeedsMore;
        }
        finally
        {
            reader.Detach();
        }
    }

    private static bool _IsAv1(Image header)
    {
        return header.Contains("heif-compression")
            && string.Equals((string)header.Get("heif-compression"), "av1", StringComparison.Ordinal);
    }

    private sealed record VipsHeader(
        VipsFormat Format,
        int Width,
        int Height,
        int Frames,
        bool HasAlpha,
        bool IsQuarterTurned
    );

    /// <summary>
    /// What a header read decided: a header, a refusal, or a request for a longer window (the whole input when
    /// <paramref name="NeedsWhole" />).
    /// </summary>
    private sealed record HeaderOutcome(VipsHeader? Header, string? Refusal, bool NeedsWhole)
    {
        public static readonly HeaderOutcome NeedsMore = new(Header: null, Refusal: null, NeedsWhole: false);

        public static HeaderOutcome Refused(string error) => new(Header: null, error, NeedsWhole: false);
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
