// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;

namespace Headless.Sitemaps;

/// <summary>Sitemap index file builder.</summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/large-sitemaps</remarks>
[PublicAPI]
public static class SitemapIndexBuilder
{
    /// <summary>Write a sitemap index file into the stream.</summary>
    public static async Task WriteToAsync(
        this IEnumerable<SitemapReference> sitemapReferences,
        Stream output,
        CancellationToken cancellationToken = default
    )
    {
        /*
         * <?xml version="1.0" encoding="UTF-8"?>
         * <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
         *   <sitemap>
         *     <loc>https://www.example.com/sitemap1.xml.gz</loc>
         *     <lastmod>2004-10-01T18:23:17+00:00</lastmod>
         *   </sitemap>
         *   <sitemap>
         *     <loc>https://www.example.com/sitemap2.xml.gz</loc>
         *   </sitemap>
         * </sitemapindex>
         */

        await using var writer = XmlWriter.Create(output, SitemapConstants.WriterSettings);
        await writer.WriteStartDocumentAsync().ConfigureAwait(false);

        await writer
            .WriteStartElementAsync(
                prefix: null,
                localName: "sitemapindex",
                ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
            )
            .ConfigureAwait(false);

        // Write sitemaps URL.
        foreach (var sitemapReference in sitemapReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _WriteSitemapRefNodeAsync(writer, sitemapReference).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task _WriteSitemapRefNodeAsync(XmlWriter writer, SitemapReference sitemapRef)
    {
        await writer.WriteStartElementAsync(prefix: null, localName: "sitemap", ns: null).ConfigureAwait(false);

        await writer
            .WriteElementStringAsync(prefix: null, localName: "loc", ns: null, value: sitemapRef.Location.AbsoluteUri)
            .ConfigureAwait(false);

        if (sitemapRef.LastModified.HasValue)
        {
            var value = sitemapRef.LastModified.Value.ToString(
                SitemapConstants.SitemapDateFormat,
                CultureInfo.InvariantCulture
            );

            await writer
                .WriteElementStringAsync(prefix: null, localName: "lastmod", ns: null, value)
                .ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }
}
