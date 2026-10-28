// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Headless.Checks;
using Headless.Http;

namespace Headless.Tus;

/// <summary>
/// The content type rule the tus stores' default header providers share: the stored object takes the upload's
/// <c>filetype</c> metadata when it is a safe, well-formed media type, and <c>application/octet-stream</c> otherwise.
/// </summary>
/// <remarks>
/// A store provider that writes its own headers provider can call <see cref="FromFileType"/> to keep the same rule.
/// </remarks>
[PublicAPI]
public static class TusContentTypes
{
    /// <summary>The metadata key tus clients (tus-js-client, Uppy) send the file's media type under.</summary>
    public const string FileTypeMetadataKey = "filetype";

    // A browser renders these as an active document, or runs them as script, when the object is opened from the
    // bucket's or CDN's origin. The client chooses the filetype, so honoring one of these would let any uploader plant
    // stored XSS on that origin; an app that must serve them writes its own headers provider.
    private static readonly FrozenSet<string> _ScriptCapableMediaTypes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ContentTypes.Texts.Html,
        ContentTypes.Applications.XhtmlXml,
        ContentTypes.Images.SvgXml,
        "text/xml",
        ContentTypes.Applications.Xml,
        "text/javascript",
        ContentTypes.Applications.Javascript,
        ContentTypes.Applications.Ecmascript,
        "text/ecmascript",
        "application/x-javascript",
        "text/x-javascript",
        "text/jscript",
        "text/xsl"
    );

    /// <summary>
    /// Returns the content type to store for an upload: its <see cref="FileTypeMetadataKey"/> metadata, normalized,
    /// when that is a well-formed media type that is not script-capable, and <c>application/octet-stream</c> otherwise.
    /// </summary>
    /// <remarks>
    /// The script-capable types fall back: <c>text/html</c>, <c>application/xhtml+xml</c>, <c>image/svg+xml</c>,
    /// <c>text/xml</c>, <c>application/xml</c>, <c>text/javascript</c>, <c>application/javascript</c>,
    /// <c>application/ecmascript</c>, <c>text/ecmascript</c>, the legacy script aliases
    /// <c>application/x-javascript</c>, <c>text/x-javascript</c>, <c>text/jscript</c> and <c>text/xsl</c>, and any type
    /// whose subtype ends in <c>+xml</c>. The
    /// comparison ignores case and parameters. The key is matched as sent, so a <c>FileType</c> key does not count.
    /// </remarks>
    /// <param name="metadata">the client's tus metadata, decoded</param>
    /// <returns>the media type to store with the finished upload</returns>
    public static string FromFileType(IReadOnlyDictionary<string, string> metadata)
    {
        Argument.IsNotNull(metadata);

        return
            metadata.TryGetValue(FileTypeMetadataKey, out var fileType)
            && _TryParse(fileType, out var parsed)
            && !_IsScriptCapable(parsed.MediaType!)
            ? parsed.ToString()
            : ContentTypes.Applications.OctetStream;
    }

    // The value is client input that becomes a stored response header, so anything that does not parse as one media
    // type (no subtype, stray characters, line breaks, a list) falls back instead of reaching storage. The parsed form
    // is what gets stored, so the type checked below is exactly the type a browser later reads.
    private static bool _TryParse(string? value, [NotNullWhen(true)] out MediaTypeHeaderValue? parsed)
    {
        parsed = null;

        return !string.IsNullOrWhiteSpace(value)
            && MediaTypeHeaderValue.TryParse(value, out parsed)
            && parsed.MediaType?.Contains('/', StringComparison.Ordinal) == true;
    }

    private static bool _IsScriptCapable(string mediaType)
    {
        // Every +xml type (RSS, Atom, MathML, XSLT, ...) is parsed as XML, which can carry XHTML or SVG script.
        return _ScriptCapableMediaTypes.Contains(mediaType)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }
}
