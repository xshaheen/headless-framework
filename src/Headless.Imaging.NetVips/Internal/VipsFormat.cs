// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Http;
using NetVips;

namespace Headless.Imaging.Internal;

/// <summary>
/// An image format the contributors read and write. The set is closed on purpose: it is the loader allowlist, so a
/// format libvips can parse but this type does not name (SVG, PDF, CSV, matrix, the native .v format, and anything
/// routed through ImageMagick) is refused before its loader runs.
/// </summary>
internal sealed class VipsFormat
{
    public static readonly VipsFormat Jpeg = new(ContentTypes.Images.Jpeg, canCompress: true, isAnimated: false);
    public static readonly VipsFormat Png = new(ContentTypes.Images.Png, canCompress: true, isAnimated: false);
    public static readonly VipsFormat Webp = new(ContentTypes.Images.Webp, canCompress: true, isAnimated: true);
    public static readonly VipsFormat Gif = new(ContentTypes.Images.Gif, canCompress: false, isAnimated: true);
    public static readonly VipsFormat Tiff = new(ContentTypes.Images.Tiff, canCompress: false, isAnimated: false);
    public static readonly VipsFormat Avif = new(ContentTypes.Images.Avif, canCompress: true, isAnimated: false);

    private static readonly VipsFormat[] _All = [Jpeg, Png, Webp, Gif, Tiff, Avif];

    private VipsFormat(string mimeType, bool canCompress, bool isAnimated)
    {
        MimeType = mimeType;
        CanCompress = canCompress;
        IsAnimated = isAnimated;
    }

    public string MimeType { get; }

    /// <summary>Gets a value indicating whether re-encoding has a quality or compression knob that can shrink the file.</summary>
    public bool CanCompress { get; }

    /// <summary>Gets a value indicating whether the format can hold several frames that must be processed one by one.</summary>
    public bool IsAnimated { get; }

    /// <summary>Gets a value indicating whether the format stores an alpha channel; JPEG is the only one that does not.</summary>
    public bool HasAlpha => this != Jpeg;

    public static VipsFormat? FromMimeType(string mimeType)
    {
        return Array.Find(_All, format => string.Equals(format.MimeType, mimeType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether <paramref name="bytes" /> start with a classic or BigTIFF byte-order mark and version number.
    /// </summary>
    public static bool HasTiffSignature(ReadOnlySpan<byte> bytes)
    {
        return bytes is [0x49, 0x49, 0x2A or 0x2B, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A or 0x2B, ..];
    }

    /// <summary>
    /// Maps the loader class libvips' content sniffing picked to a format, or <see langword="null" /> when the loader
    /// is outside the allowlist.
    /// </summary>
    /// <remarks>
    /// The names are the loader GType names <c>vips_foreign_find_load_buffer</c> returns. libvips 8.11 replaced the
    /// giflib loader (<c>VipsForeignLoadGifBuffer</c>) with libnsgif, so both names stand for GIF.
    /// </remarks>
    public static VipsFormat? FromLoader(string loader)
    {
        return loader switch
        {
            "VipsForeignLoadJpegBuffer" => Jpeg,
            "VipsForeignLoadPngBuffer" => Png,
            "VipsForeignLoadWebpBuffer" => Webp,
            "VipsForeignLoadNsgifBuffer" or "VipsForeignLoadGifBuffer" => Gif,
            "VipsForeignLoadTiffBuffer" => Tiff,
            // HEIF covers HEIC (HEVC) as well as AVIF (AV1). Only AVIF is accepted, after the header names AV1.
            "VipsForeignLoadHeifBuffer" => Avif,
            _ => null,
        };
    }

    /// <summary>
    /// Encodes <paramref name="image" /> in this format with the configured quality and metadata policy. A format
    /// without alpha gets the transparent areas flattened onto white.
    /// </summary>
    /// <remarks>
    /// libvips evaluates the whole pipeline (decode, resize, encode) inside the save call, so cancellation sets the
    /// kill flag on a private copy of the image, which stops the worker threads within a tile or two. The copy keeps
    /// the flag away from the caller's image and any operation-cache entry that shares it.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    /// <exception cref="VipsException">The image could not be decoded or encoded.</exception>
    public byte[] Save(Image image, NetVipsOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var output = HasAlpha || !image.HasAlpha() ? image.Copy() : image.Flatten(background: [_White(image)]);
        using var registration = cancellationToken.Register(
            callback: static state => ((Image)state!).SetKill(true),
            state: output
        );

        try
        {
            return _Encode(output, options);
        }
        catch (VipsException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>White in the image's own numeric range: <c>flatten</c> takes the background unscaled.</summary>
    private static double _White(Image image)
    {
        return image.Interpretation is Enums.Interpretation.Rgb16 or Enums.Interpretation.Grey16 ? 65535 : 255;
    }

    private byte[] _Encode(Image image, NetVipsOptions options)
    {
        var keep = options.StripMetadata ? Enums.ForeignKeep.Icc : Enums.ForeignKeep.All;
        var subsample = options.ChromaSubsampling switch
        {
            NetVipsChromaSubsampling.On => Enums.ForeignSubsample.On,
            NetVipsChromaSubsampling.Off => Enums.ForeignSubsample.Off,
            _ => Enums.ForeignSubsample.Auto,
        };

        if (this == Jpeg)
        {
            // Optimized Huffman tables cost a little CPU and save a few percent on every file, losslessly.
            return image.JpegsaveBuffer(
                q: options.JpegQuality,
                optimizeCoding: true,
                interlace: options.JpegProgressive,
                subsampleMode: subsample,
                keep: keep
            );
        }

        if (this == Png)
        {
            return image.PngsaveBuffer(compression: options.PngCompressionLevel, keep: keep);
        }

        if (this == Webp)
        {
            return image.WebpsaveBuffer(q: options.WebpQuality, keep: keep);
        }

        if (this == Gif)
        {
            return image.GifsaveBuffer(keep: keep);
        }

        if (this == Tiff)
        {
            return image.TiffsaveBuffer(keep: keep);
        }

        return image.HeifsaveBuffer(
            q: options.AvifQuality,
            compression: Enums.ForeignHeifCompression.Av1,
            subsampleMode: subsample,
            keep: keep
        );
    }
}
