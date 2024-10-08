// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Xml;

namespace Framework.Generator.Primitives.Helpers;

internal static class XmlDocumentExtensions
{
    public static XmlDocument LoadXmlDocument(this string xml)
    {
        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(xml);

        return xmlDoc;
    }
}
