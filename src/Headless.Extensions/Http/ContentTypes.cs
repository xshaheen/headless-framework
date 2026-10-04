// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Well-known MIME (media) type strings, grouped by top-level type, for use in HTTP
/// <c>Content-Type</c> / <c>Accept</c> headers and content negotiation.
/// </summary>
[PublicAPI]
public static class ContentTypes
{
    /// <summary>Image media types (<c>image/*</c>).</summary>
    public static class Images
    {
        /// <summary>Icon content type.</summary>
        public const string Icon = "image/x-icon";

        /// <summary>Graphics Interchange Format (GIF) image (.gif); Defined in RFC 2045 and RFC 2046.</summary>
        public const string Gif = "image/gif";

        /// <summary>Windows OS/2 Bitmap Graphics (BMP) image (.bmp, .dib).</summary>
        public const string Bmp = "image/bmp";

        /// <summary>SVG image content type.</summary>
        public const string SvgXml = "image/svg+xml";

        /// <summary>Joint Photographic Expert Group (JPEG) image (.jpg, .jpeg, .jfif, .pjpeg, .pjp); Defined in RFC 2045 and RFC 2046.</summary>
        public const string Jpeg = "image/jpeg";

        /// <summary>Portable Network Graphics; Registered,[8] Defined in RFC 2083.</summary>
        public const string Png = "image/png";

        /// <summary>Animated Portable Network Graphics (APNG) image (.apng).</summary>
        public const string Apng = "image/apng";

        /// <summary>AV1 Image File Format (.avif).</summary>
        public const string Avif = "image/avif";

        /// <summary>Tagged Image File Format (TIFF) (.tif, .tiff).</summary>
        public const string Tiff = "image/tiff";

        /// <summary>Web Picture format (WebP) (.webp).</summary>
        public const string Webp = "image/webp";
    }

    /// <summary>Audio media types (<c>audio/*</c>).</summary>
    public static class Audios
    {
        /// <summary>MIDI audio (<c>audio/midi</c>).</summary>
        public const string Midi = "audio/midi";

        /// <summary>MP4 audio (<c>audio/mp4</c>).</summary>
        public const string Mp4 = "audio/mp4";

        /// <summary>MPEG audio (<c>audio/mpeg</c>).</summary>
        public const string Mpeg = "audio/mpeg";

        /// <summary>Ogg audio (<c>audio/ogg</c>).</summary>
        public const string Ogg = "audio/ogg";

        /// <summary>WebM audio (<c>audio/webm</c>).</summary>
        public const string Webm = "audio/webm";

        /// <summary>Advanced Audio Coding (AAC) audio (<c>audio/x-aac</c>).</summary>
        public const string XAac = "audio/x-aac";

        /// <summary>Audio Interchange File Format (AIFF) (<c>audio/x-aiff</c>).</summary>
        public const string XAiff = "audio/x-aiff";

        /// <summary>M3U playlist (<c>audio/x-mpegurl</c>).</summary>
        public const string XMpegurl = "audio/x-mpegurl";

        /// <summary>Windows Media Audio (WMA) (<c>audio/x-ms-wma</c>).</summary>
        public const string XMsWma = "audio/x-ms-wma";

        /// <summary>Waveform Audio (WAV) (<c>audio/x-wav</c>).</summary>
        public const string XWav = "audio/x-wav";

        /// <summary>AC3 audio file (.ac3)</summary>
        public const string Ac3 = "audio/vnd.dolby.dd-raw";
    }

    /// <summary>Text media types (<c>text/*</c>).</summary>
    public static class Texts
    {
        /// <summary>Textual data; Defined in RFC 2046 and RFC 3676.</summary>
        public const string Plain = "text/plain";

        /// <summary>HTML; Defined in RFC 2854.</summary>
        public const string Html = "text/html";

        /// <summary>Cascading Style Sheets (<c>text/css</c>).</summary>
        public const string Css = "text/css";

        /// <summary>Comma-separated values (<c>text/csv</c>).</summary>
        public const string Csv = "text/csv";

        /// <summary>Rich text format (<c>text/richtext</c>).</summary>
        public const string RichText = "text/richtext";

        /// <summary>Standard Generalized Markup Language (<c>text/sgml</c>).</summary>
        public const string Sgml = "text/sgml";

        /// <summary>YAML Ain't Markup Language (<c>text/yaml</c>).</summary>
        public const string Yaml = "text/yaml";
    }

    /// <summary>Video media types (<c>video/*</c>).</summary>
    public static class Videos
    {
        /// <summary>3GPP multimedia container (<c>video/3gpp</c>).</summary>
        public const string Threegpp = "video/3gpp";

        /// <summary>H.264/AVC video (<c>video/h264</c>).</summary>
        public const string H264 = "video/h264";

        /// <summary>MP4 video (<c>video/mp4</c>).</summary>
        public const string Mp4 = "video/mp4";

        /// <summary>MPEG video (<c>video/mpeg</c>).</summary>
        public const string Mpeg = "video/mpeg";

        /// <summary>Ogg video (<c>video/ogg</c>).</summary>
        public const string Ogg = "video/ogg";

        /// <summary>QuickTime video (<c>video/quicktime</c>).</summary>
        public const string Quicktime = "video/quicktime";

        /// <summary>WEBM video (.webm).</summary>
        public const string Webm = "video/webm";
    }

    /// <summary>Application media types (<c>application/*</c> and a few related multipart types).</summary>
    public static class Applications
    {
        /// <summary>Arbitrary binary data (<c>application/octet-stream</c>); the default for unrecognized file types.</summary>
        public const string OctetStream = "application/octet-stream";

        /// <summary>Form URL Encoded.</summary>
        public const string XWwwFormUrlencoded = "application/x-www-form-urlencoded";

        /// <summary>Apple macOS disk image (DMG) (.dmg)</summary>
        public const string Dmg = "application/x-apple-diskimage";

        /// <summary>DICOM image (.dcm).</summary>
        public const string Dicom = "application/dicom";

        /// <summary>WebAssembly (.wasm)</summary>
        public const string Wasm = "application/wasm";

        /// <summary>Atom feeds.</summary>
        public const string AtomXml = "application/atom+xml";

        /// <summary>Multi-part form data; Defined in RFC 2388.</summary>
        public const string MultipartFormData = "multipart/form-data";

        /// <summary>Extensible Markup Language; Defined in RFC 3023.</summary>
        public const string Xml = "application/xml";

        /// <summary>JavaScript Object Notation JSON; Defined in RFC 4627.</summary>
        public const string Json = "application/json";

        /// <summary>Problem Details JavaScript Object Notation (JSON); Defined at https://tools.ietf.org/html/rfc7807.</summary>
        public const string ProblemJson = "application/problem+json";

        /// <summary>Problem Details Extensible Markup Language (XML); Defined at https://tools.ietf.org/html/rfc7807.</summary>
        public const string ProblemXml = "application/problem+xml";

        /// <summary>JSON Patch; Defined at http://jsonpatch.com/.</summary>
        public const string JsonPatch = "application/json-patch+json";

        /// <summary>Web App Manifest.</summary>
        public const string Manifest = "application/manifest+json";

        /// <summary>REST'ful JavaScript Object Notation (JSON); Defined at http://restfuljson.org/.</summary>
        public const string RestfulJson = "application/vnd.restful+json";

        /// <summary>Rich Site Summary; Defined by Harvard Law.</summary>
        public const string RssXml = "application/rss+xml";

        /// <summary>Rich Text Format (.rtf).</summary>
        public const string Rtf = "application/rtf";

        /// <summary>Windows executable file (.exe).</summary>
        public const string Exe = "application/x-msdownload";

        /// <summary>Compressed ZIP.</summary>
        public const string Zip = "application/zip";

        /// <summary>Electronic publication (EPUB) file (.epub).</summary>
        public const string Epub = "application/epub+zip";

        /// <summary>Adobe portable document format (PDF) (.pdf).</summary>
        public const string Pdf = "application/pdf";

        /// <summary>Microsoft Excel (xls).</summary>
        public const string Xls = "application/vnd.ms-excel";

        /// <summary>Microsoft Excel (OpenXML) (.xlsx).</summary>
        public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>Microsoft Word (.doc).</summary>
        public const string Doc = "application/msword";

        /// <summary>Microsoft Word (OpenXML) (.docx).</summary>
        public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        /// <summary>Microsoft PowerPoint (.ppt).</summary>
        public const string PPt = "application/vnd.ms-powerpoint";

        /// <summary>Microsoft PowerPoint (OpenXML) (.pptx).</summary>
        public const string PPtx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        /// <summary>Microsoft Visio (.vsd, .vsdx).</summary>
        public const string Vsd = "application/vnd.visio";

        /// <summary>Microsoft Outlook message (.msg).</summary>
        public const string Msg = "application/vnd.ms-outlook";

        /// <summary>XML paper specification document (OpenXML) (.xps).</summary>
        public const string Xps = "application/vnd.ms-xpsdocument";

        /// <summary>Open document text (ODT) for office applications (.odt).</summary>
        public const string Odt = "application/vnd.oasis.opendocument.text";

        /// <summary>Open document spreadsheet (ODS) for office applications (.ods).</summary>
        public const string Ods = "application/vnd.oasis.opendocument.spreadsheet";

        /// <summary>Open document presentation (ODP) for office applications (.odp).</summary>
        public const string Odp = "application/vnd.oasis.opendocument.presentation";

        /// <summary>XHTML (<c>application/xhtml+xml</c>).</summary>
        public const string XhtmlXml = "application/xhtml+xml";

        /// <summary>XML document type definition (<c>application/xml-dtd</c>).</summary>
        public const string XmlDtd = "application/xml-dtd";

        /// <summary>XSLT stylesheet (<c>application/xslt+xml</c>).</summary>
        public const string XsltXml = "application/xslt+xml";

        /// <summary>Atom feed catalog (<c>application/atomcat+xml</c>).</summary>
        public const string AtomcatXml = "application/atomcat+xml";

        /// <summary>ECMAScript (JavaScript) (<c>application/ecmascript</c>).</summary>
        public const string Ecmascript = "application/ecmascript";

        /// <summary>Java archive (JAR) (<c>application/java-archive</c>).</summary>
        public const string JavaArchive = "application/java-archive";

        /// <summary>JavaScript (<c>application/javascript</c>).</summary>
        public const string Javascript = "application/javascript";

        /// <summary>MP4 container (<c>application/mp4</c>).</summary>
        public const string Mp4 = "application/mp4";

        /// <summary>PKCS #10 certification request (<c>application/pkcs10</c>).</summary>
        public const string Pkcs10 = "application/pkcs10";

        /// <summary>PKCS #7 MIME message (<c>application/pkcs7-mime</c>).</summary>
        public const string Pkcs7Mime = "application/pkcs7-mime";

        /// <summary>PKCS #7 detached signature (<c>application/pkcs7-signature</c>).</summary>
        public const string Pkcs7Signature = "application/pkcs7-signature";

        /// <summary>PKCS #8 private key (<c>application/pkcs8</c>).</summary>
        public const string Pkcs8 = "application/pkcs8";

        /// <summary>PostScript document (<c>application/postscript</c>).</summary>
        public const string Postscript = "application/postscript";

        /// <summary>Resource Description Framework (RDF) in XML (<c>application/rdf+xml</c>).</summary>
        public const string RdfXml = "application/rdf+xml";

        /// <summary>Synchronized Multimedia Integration Language (SMIL) in XML (<c>application/smil+xml</c>).</summary>
        public const string SmilXml = "application/smil+xml";

        /// <summary>OpenType font (<c>application/x-font-otf</c>).</summary>
        public const string XFontOtf = "application/x-font-otf";

        /// <summary>TrueType font (<c>application/x-font-ttf</c>).</summary>
        public const string XFontTtf = "application/x-font-ttf";

        /// <summary>Web Open Font Format (WOFF) (<c>application/x-font-woff</c>).</summary>
        public const string XFontWoff = "application/x-font-woff";

        /// <summary>PKCS #12 certificate bundle (.p12, .pfx) (<c>application/x-pkcs12</c>).</summary>
        public const string XPkcs12 = "application/x-pkcs12";

        /// <summary>Adobe Shockwave Flash (<c>application/x-shockwave-flash</c>).</summary>
        public const string XShockwaveFlash = "application/x-shockwave-flash";

        /// <summary>Microsoft Silverlight application (<c>application/x-silverlight-app</c>).</summary>
        public const string XSilverlightApp = "application/x-silverlight-app";
    }
}
