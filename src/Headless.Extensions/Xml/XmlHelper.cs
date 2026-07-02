// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Headless.Xml;

/// <summary>Helpers for XML-encoding and decoding text, serializing values, and validating XML safely.</summary>
[PublicAPI]
public static class XmlHelper
{
    // Hardened reader shared by both overloads: DtdProcessing.Ignore + XmlResolver=null skip any inline DTD,
    // so entity-expansion (billion-laughs) and external-entity (XXE) payloads can never be processed.
    private static readonly XmlReaderSettings _SafeReaderSettings = new()
    {
        CheckCharacters = true,
        ConformanceLevel = ConformanceLevel.Document,
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        ValidationFlags = XmlSchemaValidationFlags.None,
        ValidationType = ValidationType.None,
        XmlResolver = null,
    };

    /// <summary>Strips hidden/control characters from <paramref name="str"/> and then XML-encodes the result.</summary>
    /// <param name="str">The text to sanitize and encode; may be <see langword="null"/>.</param>
    /// <param name="settings">Optional writer settings; a sensible async default is used when <see langword="null"/>.</param>
    /// <returns>The XML-encoded text, or <see langword="null"/> when <paramref name="str"/> is <see langword="null"/>.</returns>
    public static async Task<string?> XmlEncodeAsync(string? str, XmlWriterSettings? settings = null)
    {
        return str is not null
            ? await XmlEncodeAsIsAsync(str.RemoveHiddenChars(), settings).ConfigureAwait(false)
            : null;
    }

    /// <summary>XML-encodes <paramref name="str"/> without first stripping hidden/control characters.</summary>
    /// <param name="str">The text to encode; may be <see langword="null"/>.</param>
    /// <param name="settings">Optional writer settings; a sensible async default is used when <see langword="null"/>.</param>
    /// <returns>The XML-encoded text, or <see langword="null"/> when <paramref name="str"/> is <see langword="null"/>.</returns>
    /// <exception cref="System.Xml.XmlException">Thrown when <paramref name="str"/> contains characters that are not valid in XML 1.0.</exception>
    public static async Task<string?> XmlEncodeAsIsAsync(string? str, XmlWriterSettings? settings = null)
    {
        if (str is null)
        {
            return null;
        }

        settings ??= new XmlWriterSettings { Async = true, ConformanceLevel = ConformanceLevel.Auto };

        await using var sw = new StringWriter();
        await using var xwr = XmlWriter.Create(sw, settings);
        await xwr.WriteStringAsync(str).ConfigureAwait(false);
        await xwr.FlushAsync().ConfigureAwait(false);

        return sw.ToString();
    }

    /// <summary>Decodes XML/HTML entity references in <paramref name="input"/> back to their literal characters.</summary>
    /// <param name="input">The encoded text to decode.</param>
    /// <returns>The decoded text.</returns>
    public static string XmlDecode(string input)
    {
        return WebUtility.HtmlDecode(input);
    }

    /// <summary>Serializes a <see cref="DateTime"/> value to its XML representation.</summary>
    /// <param name="dateTime">The value to serialize.</param>
    /// <returns>The XML document representing <paramref name="dateTime"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when serialization fails (for example when the underlying writer faults).</exception>
    [RequiresUnreferencedCode("XmlSerializer uses reflection which is not compatible with trimming.")]
    [RequiresDynamicCode("XmlSerializer requires dynamic code generation.")]
    public static async Task<string> SerializeDateTimeAsync(DateTime dateTime)
    {
        var xmlS = new XmlSerializer(typeof(DateTime));
        var sb = new StringBuilder();
        await using var sw = new StringWriter(sb);
        xmlS.Serialize(sw, dateTime);

        return sb.ToString();
    }

    #region Is Valid Xml

    /// <summary>Determines whether <paramref name="maybeXml"/> is well-formed XML, parsing with a hardened (XXE-safe) reader.</summary>
    /// <param name="maybeXml">The candidate XML text.</param>
    /// <returns><see langword="true"/> when the text parses as well-formed XML; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="maybeXml"/> is <see langword="null"/>.</exception>
    public static bool IsValidXml(string maybeXml)
    {
        using var xmlReader = XmlReader.Create(new StringReader(maybeXml), _SafeReaderSettings);

        return _TryReadToEnd(xmlReader);
    }

    /// <summary>Determines whether <paramref name="maybeXml"/> contains well-formed XML, parsing with a hardened (XXE-safe) reader.</summary>
    /// <param name="maybeXml">The stream containing the candidate XML.</param>
    /// <returns><see langword="true"/> when the stream parses as well-formed XML; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="maybeXml"/> is <see langword="null"/>.</exception>
    public static bool IsValidXml(Stream maybeXml)
    {
        using var xmlReader = XmlReader.Create(maybeXml, _SafeReaderSettings);

        return _TryReadToEnd(xmlReader);
    }

    /// <summary>Determines whether <paramref name="maybeXml"/> is well-formed XML, parsing with a hardened (XXE-safe) reader.</summary>
    /// <param name="maybeXml">The candidate XML text.</param>
    /// <returns><see langword="true"/> when the text parses as well-formed XML; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="maybeXml"/> is <see langword="null"/>.</exception>
    public static async Task<bool> IsValidXmlAsync(string maybeXml)
    {
        using var xmlReader = XmlReader.Create(new StringReader(maybeXml), _SafeReaderSettings);

        return await _TryReadToEndAsync(xmlReader).ConfigureAwait(false);
    }

    /// <summary>Determines whether <paramref name="maybeXml"/> contains well-formed XML, parsing with a hardened (XXE-safe) reader.</summary>
    /// <param name="maybeXml">The stream containing the candidate XML.</param>
    /// <returns><see langword="true"/> when the stream parses as well-formed XML; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="maybeXml"/> is <see langword="null"/>.</exception>
    public static async Task<bool> IsValidXmlAsync(Stream maybeXml)
    {
        using var xmlReader = XmlReader.Create(maybeXml, _SafeReaderSettings);

        return await _TryReadToEndAsync(xmlReader).ConfigureAwait(false);
    }

    private static bool _TryReadToEnd(XmlReader xmlReader)
    {
        try
        {
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
            while (xmlReader.Read())
#pragma warning restore MA0045
            {
                // This space intentionally left blank
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static async Task<bool> _TryReadToEndAsync(XmlReader xmlReader)
    {
        try
        {
            while (await xmlReader.ReadAsync().ConfigureAwait(false))
            {
                // This space intentionally left blank
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    #endregion
}
