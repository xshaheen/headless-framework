// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>The media type of a captured attachment, as the real MIME converter would send it.</summary>
internal static class EmailAttachmentTypes
{
    /// <summary>
    /// The attachment's captured content type when the caller gave one, else the type its file name implies, reduced
    /// to the lower-case media type without parameters.
    /// </summary>
    public static string Resolve(CapturedEmailAttachment attachment)
    {
        var type = string.IsNullOrWhiteSpace(attachment.ContentType)
            ? string.IsNullOrEmpty(attachment.FileName)
                ? EmailAttachmentContentType.Default
                : EmailAttachmentContentType.Resolve(attachment.FileName)
            : attachment.ContentType;

        var separator = type.IndexOf(';', StringComparison.Ordinal);
        var media = (separator < 0 ? type : type[..separator]).Trim();

#pragma warning disable CA1308 // False positive: media types are case-insensitive, and the checks below compare the lower-case form.
        return media.Length == 0 ? EmailAttachmentContentType.Default : media.ToLowerInvariant();
#pragma warning restore CA1308
    }

    /// <summary>
    /// Whether a preview may inline an attachment of <paramref name="mediaType"/> as a <c>data:</c> image: a raster
    /// image only, because an SVG carries script.
    /// </summary>
    public static bool IsInlineImage(string mediaType) =>
        mediaType.StartsWith("image/", StringComparison.Ordinal)
        && !string.Equals(mediaType, "image/svg+xml", StringComparison.Ordinal);

    /// <summary>
    /// The content type a download serves for <paramref name="mediaType"/>. A captured type is caller-supplied, so an
    /// HTML, XML, SVG, or script type is served as opaque bytes: opened in the browser, it would run in the dashboard's
    /// origin.
    /// </summary>
    public static string Served(string mediaType) =>
        mediaType.Contains("html", StringComparison.Ordinal)
        || mediaType.Contains("xml", StringComparison.Ordinal)
        || mediaType.Contains("svg", StringComparison.Ordinal)
        || mediaType.Contains("script", StringComparison.Ordinal)
        || string.Equals(mediaType, "text/xsl", StringComparison.Ordinal)
            ? EmailAttachmentContentType.Default
            : mediaType;
}
