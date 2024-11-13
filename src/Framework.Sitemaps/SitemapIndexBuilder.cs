// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;

namespace Framework.Sitemaps;

/// <summary>Sitemap index file builder.</summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/large-sitemaps</remarks>
[PublicAPI]
public static class SitemapIndexBuilder
{
    /// <summary>Write sitemap index file into the stream.</summary>
    public static async Task WriteToAsync(this IEnumerable<SitemapReference> sitemapReferences, Stream output)
    {
        await using var writer = XmlWriter.Create(output, SitemapConstants.WriterSettings);
        await writer.WriteStartDocumentAsync();

        await writer.WriteStartElementAsync(
            prefix: null,
            localName: "sitemapindex",
            ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
        );

        // Write sitemaps URL.
        foreach (var sitemapReference in sitemapReferences)
        {
            await _WriteSitemapRefNodeAsync(writer, sitemapReference);
        }

        await writer.WriteEndElementAsync();
    }

    private static async Task _WriteSitemapRefNodeAsync(XmlWriter writer, SitemapReference sitemapRef)
    {
        await writer.WriteStartElementAsync(prefix: null, localName: "sitemap", ns: null);
        await writer.WriteElementStringAsync(
            prefix: null,
            localName: "loc",
            ns: null,
            value: sitemapRef.Location.AbsoluteUri
        );

        if (sitemapRef.LastModified.HasValue)
        {
            var value = sitemapRef.LastModified.Value.ToString(
                SitemapConstants.SitemapDateFormat,
                CultureInfo.InvariantCulture
            );

            await writer.WriteElementStringAsync(prefix: null, localName: "lastmod", ns: null, value);
        }

        await writer.WriteEndElementAsync();
    }
}
