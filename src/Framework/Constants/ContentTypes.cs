// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Constants;

[PublicAPI]
public static class ContentTypes
{
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

    public static class Audios
    {
        public const string Midi = "audio/midi";
        public const string Mp4 = "audio/mp4";
        public const string Mpeg = "audio/mpeg";
        public const string Ogg = "audio/ogg";
        public const string Webm = "audio/webm";
        public const string XAac = "audio/x-aac";
        public const string XAiff = "audio/x-aiff";
        public const string XMpegurl = "audio/x-mpegurl";
        public const string XMsWma = "audio/x-ms-wma";
        public const string XWav = "audio/x-wav";

        /// <summary>AC3 audio file (.ac3)</summary>
        public const string Ac3 = "audio/vnd.dolby.dd-raw";
    }

    public static class Texts
    {
        /// <summary>Textual data; Defined in RFC 2046 and RFC 3676.</summary>
        public const string Plain = "text/plain";

        /// <summary>HTML; Defined in RFC 2854.</summary>
        public const string Html = "text/html";

        public const string Css = "text/css";

        public const string Csv = "text/csv";

        public const string RichText = "text/richtext";

        public const string Sgml = "text/sgml";

        public const string Yaml = "text/yaml";
    }

    public static class Videos
    {
        public const string Threegpp = "video/3gpp";
        public const string H264 = "video/h264";
        public const string Mp4 = "video/mp4";
        public const string Mpeg = "video/mpeg";
        public const string Ogg = "video/ogg";
        public const string Quicktime = "video/quicktime";

        /// <summary>WEBM video (.webm).</summary>
        public const string Webm = "video/webm";
    }

    public static class Applications
    {
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

        /// <summary>Multi-part form daata; Defined in RFC 2388.</summary>
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

        public const string XhtmlXml = "application/xhtml+xml";
        public const string XmlDtd = "application/xml-dtd";
        public const string XsltXml = "application/xslt+xml";
        public const string AtomcatXml = "application/atomcat+xml";
        public const string Ecmascript = "application/ecmascript";
        public const string JavaArchive = "application/java-archive";
        public const string Javascript = "application/javascript";
        public const string Mp4 = "application/mp4";
        public const string Pkcs10 = "application/pkcs10";
        public const string Pkcs7Mime = "application/pkcs7-mime";
        public const string Pkcs7Signature = "application/pkcs7-signature";
        public const string Pkcs8 = "application/pkcs8";
        public const string Postscript = "application/postscript";
        public const string RdfXml = "application/rdf+xml";
        public const string SmilXml = "application/smil+xml";
        public const string XFontOtf = "application/x-font-otf";
        public const string XFontTtf = "application/x-font-ttf";
        public const string XFontWoff = "application/x-font-woff";
        public const string XPkcs12 = "application/x-pkcs12";
        public const string XShockwaveFlash = "application/x-shockwave-flash";
        public const string XSilverlightApp = "application/x-silverlight-app";
    }
}
