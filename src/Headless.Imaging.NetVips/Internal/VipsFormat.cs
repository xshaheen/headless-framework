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

    /// <summary>Gets a value indicating whether the format stores an alpha channel, so padding can be transparent.</summary>
    public bool HasAlpha => this != Jpeg;

    public static VipsFormat? FromMimeType(string mimeType)
    {
        return Array.Find(_All, format => string.Equals(format.MimeType, mimeType, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Encodes <paramref name="image" /> in this format with the configured quality and metadata policy.</summary>
    public byte[] Save(Image image, NetVipsOptions options)
    {
        var keep = options.StripMetadata ? Enums.ForeignKeep.Icc : Enums.ForeignKeep.All;

        if (this == Jpeg)
        {
            // Optimized Huffman tables cost a little CPU and save a few percent on every file, losslessly.
            return image.JpegsaveBuffer(q: options.JpegQuality, optimizeCoding: true, keep: keep);
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

        return image.HeifsaveBuffer(q: options.AvifQuality, compression: Enums.ForeignHeifCompression.Av1, keep: keep);
    }
}
