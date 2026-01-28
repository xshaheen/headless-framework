// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Headless.Xml;

[PublicAPI]
public static class XmlHelper
{
    /// <summary>Remove hidden characters then XML Encode</summary>
    public static async Task<string?> XmlEncodeAsync(string? str, XmlWriterSettings? settings = null)
    {
        return str is not null ? await XmlEncodeAsIsAsync(str.RemoveHiddenChars(), settings) : null;
    }

    /// <summary>XML Encode as is</summary>
    public static async Task<string?> XmlEncodeAsIsAsync(string? str, XmlWriterSettings? settings = null)
    {
        if (str is null)
        {
            return null;
        }

        settings ??= new XmlWriterSettings { Async = true, ConformanceLevel = ConformanceLevel.Auto };

        await using var sw = new StringWriter();
        await using var xwr = XmlWriter.Create(sw, settings);
        await xwr.WriteStringAsync(str);
        await xwr.FlushAsync();

        return sw.ToString();
    }

    /// <summary>Decodes an attribute</summary>
    public static string XmlDecode(string input)
    {
        return WebUtility.HtmlDecode(input);
    }

    /// <summary>Serializes a datetime</summary>
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

    public static bool IsValidXml(string maybeXml)
    {
        try
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(maybeXml);

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static bool IsValidXml(Stream maybeXml)
    {
        var settings = new XmlReaderSettings
        {
            CheckCharacters = true,
            ConformanceLevel = ConformanceLevel.Document,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            ValidationFlags = XmlSchemaValidationFlags.None,
            ValidationType = ValidationType.None,
        };

        using var xmlReader = XmlReader.Create(maybeXml, settings);

        try
        {
            while (xmlReader.Read())
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
}
