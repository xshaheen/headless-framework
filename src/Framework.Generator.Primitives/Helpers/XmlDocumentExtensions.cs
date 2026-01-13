// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;

namespace Framework.Generator.Primitives.Helpers;

internal static class XmlDocumentExtensions
{
    public static XmlDocument LoadXmlDocument(this string xml)
    {
        var xmlDoc = new XmlDocument { XmlResolver = null };
        xmlDoc.LoadXml(xml);

        return xmlDoc;
    }
}
