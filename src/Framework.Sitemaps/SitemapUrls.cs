// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Xml;

namespace Framework.Sitemaps;

/// <summary>Sitemap file builder</summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/build-sitemap</remarks>
[PublicAPI]
public static class SitemapUrls
{
    /// <summary>
    /// Generate sitemap and separate it if exceeded max urls in single file.
    /// <see cref="SitemapConstants.MaxSitemapUrls"/>
    /// </summary>
    public static async Task<List<MemoryStream>> WriteAsync(this IReadOnlyCollection<SitemapUrl> sitemapUrls)
    {
        // split URLs into separate lists based on the max size
        var sitemaps = sitemapUrls
            .Select((url, index) => new { Index = index, Value = url })
            .GroupBy(group => group.Index / SitemapConstants.MaxSitemapUrls)
            .Select(group => group.Select(url => url.Value).ToArray());

        var streams = new List<MemoryStream>();

        foreach (var sitemap in sitemaps)
        {
            var stream = new MemoryStream();
            await sitemap.WriteToAsync(stream);
            streams.Add(stream);
        }

        return streams;
    }

    /// <summary>Write a sitemap file into the stream.</summary>
    public static async Task WriteToAsync(this IReadOnlyCollection<SitemapUrl> sitemapUrls, Stream output)
    {
        /*
         * <?xml version="1.0" encoding="UTF-8"?>
         * <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
         *   <url>
         *     <loc>https://www.example.com/foo.html</loc>
         *     <lastmod>2022-06-04</lastmod>
         *   </url>
         * </urlset>
         */

        await using var writer = XmlWriter.Create(output, SitemapConstants.WriterSettings);
        await writer.WriteStartDocumentAsync();

        await writer.WriteStartElementAsync(
            prefix: null,
            localName: "urlset",
            ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
        );

        // Add xmlns:xhtml attribute if there are alternate URLs
        var hasAlternateUrls = sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.AlternateLocations is not null);

        if (hasAlternateUrls)
        {
            await writer.WriteAttributeStringAsync(
                prefix: "xmlns",
                localName: "xhtml",
                ns: null,
                value: "http://www.w3.org/1999/xhtml"
            );
        }

        // Add xmlns:image attribute if there are images
        var hasImages = sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.Images is not null);

        if (hasImages)
        {
            await writer.WriteAttributeStringAsync(
                prefix: "xmlns",
                localName: "image",
                ns: null,
                value: "http://www.google.com/schemas/sitemap-image/1.1"
            );
        }

        // write URLs to the sitemap
        foreach (var sitemapUrl in sitemapUrls)
        {
            await _WriteUrlNodeAsync(writer: writer, sitemapUrl: sitemapUrl);
        }

        await writer.WriteEndElementAsync();
    }

    #region Helpers

    private static async Task _WriteUrlNodeAsync(XmlWriter writer, SitemapUrl sitemapUrl)
    {
        var hasAlternates = sitemapUrl.AlternateLocations is not null;

        if (!hasAlternates)
        {
            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null);

            await writer.WriteElementStringAsync(
                prefix: null,
                localName: "loc",
                ns: null,
                value: sitemapUrl.Location!.AbsoluteUri
            );

            if (sitemapUrl.Images is not null)
            {
                await _WriteImagesAsync(writer, sitemapUrl.Images);
            }

            _WriteOtherNodes(writer, sitemapUrl);
            await writer.WriteEndElementAsync();

            return;
        }

        // write with alternates
        Debug.Assert(sitemapUrl.AlternateLocations is not null);

        var filteredAlternates = sitemapUrl.WriteAlternateLanguageCodes is null
            ? sitemapUrl.AlternateLocations
            : sitemapUrl.AlternateLocations.Where(predicate: alt =>
                sitemapUrl.WriteAlternateLanguageCodes!.Contains(alt.LanguageCode, StringComparer.OrdinalIgnoreCase)
            );

        foreach (var url in filteredAlternates)
        {
            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null);

            await writer.WriteElementStringAsync(
                prefix: null,
                localName: "loc",
                ns: null,
                value: url.Location.AbsoluteUri
            );

            // Write images in each alternate URL
            if (sitemapUrl.Images is not null)
            {
                await _WriteImagesAsync(writer, sitemapUrl.Images);
            }

            // Write alternate URLs
            await _WriteAlternateUrlsReferenceAsync(writer, sitemapUrl.AlternateLocations);

            // Write properties
            _WriteOtherNodes(writer, sitemapUrl);

            await writer.WriteEndElementAsync();
        }
    }

    private static async Task _WriteAlternateUrlsReferenceAsync(
        XmlWriter writer,
        IEnumerable<SitemapAlternateUrl> alternateUrls
    )
    {
        foreach (var alternate in alternateUrls)
        {
            await writer.WriteStartElementAsync(prefix: "xhtml", localName: "link", ns: null);
            await writer.WriteAttributeStringAsync(prefix: null, localName: "rel", ns: null, value: "alternate");

            await writer.WriteAttributeStringAsync(
                prefix: null,
                localName: "hreflang",
                ns: null,
                value: alternate.LanguageCode
            );

            await writer.WriteAttributeStringAsync(
                prefix: null,
                localName: "href",
                ns: null,
                value: alternate.Location.AbsoluteUri
            );

            await writer.WriteEndElementAsync();
        }
    }

    private static async Task _WriteImagesAsync(XmlWriter writer, IEnumerable<SitemapImage> images)
    {
        /*
         * <image:image>
         *     <image:loc>https://example.com/image.jpg</image:loc>
         * </image:image>
         */

        foreach (var image in images)
        {
            await writer.WriteStartElementAsync(prefix: "image", localName: "image", ns: null);

            await writer.WriteElementStringAsync(
                prefix: "image",
                localName: "loc",
                ns: null,
                value: image.Location.AbsoluteUri
            );

            await writer.WriteEndElementAsync();
        }
    }

    private static void _WriteOtherNodes(XmlWriter writer, SitemapUrl sitemapUrl)
    {
        if (sitemapUrl.Priority is not null)
        {
            var value = sitemapUrl.Priority.Value.ToString(format: "N1", provider: CultureInfo.InvariantCulture);
            writer.WriteElementString(localName: "priority", value);
        }

        if (sitemapUrl.ChangeFrequency is not null)
        {
            var value = sitemapUrl.ChangeFrequency.Value.ToString().ToLowerInvariant();
            writer.WriteElementString(localName: "changefreq", value);
        }

        if (sitemapUrl.LastModified is not null)
        {
            var value = sitemapUrl.LastModified.Value.ToString(
                format: SitemapConstants.SitemapDateFormat,
                provider: CultureInfo.InvariantCulture
            );

            writer.WriteElementString(localName: "lastmod", value);
        }
    }

    #endregion
}
