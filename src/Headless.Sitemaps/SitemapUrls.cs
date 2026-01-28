// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Xml;

namespace Headless.Sitemaps;

/// <summary>Sitemap file builder</summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/build-sitemap</remarks>
[PublicAPI]
public static class SitemapUrls
{
    /// <summary>
    /// Generate sitemap and separate it if exceeded max urls in single file.
    /// <see cref="SitemapConstants.MaxSitemapUrls"/>
    /// </summary>
    public static async Task<List<MemoryStream>> WriteAsync(
        this IReadOnlyCollection<SitemapUrl> sitemapUrls,
        CancellationToken cancellationToken = default
    )
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
            await sitemap.WriteToAsync(stream, cancellationToken).AnyContext();
            streams.Add(stream);
        }

        return streams;
    }

    /// <summary>Write a sitemap file into the stream.</summary>
    public static async Task WriteToAsync(
        this IReadOnlyCollection<SitemapUrl> sitemapUrls,
        Stream output,
        CancellationToken cancellationToken = default
    )
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
        await writer.WriteStartDocumentAsync().AnyContext();

        await writer
            .WriteStartElementAsync(
                prefix: null,
                localName: "urlset",
                ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
            )
            .AnyContext();

        // Add xmlns:xhtml attribute if there are alternate URLs
        var hasAlternateUrls = sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.AlternateLocations is not null);

        if (hasAlternateUrls)
        {
            await writer
                .WriteAttributeStringAsync(
                    prefix: "xmlns",
                    localName: "xhtml",
                    ns: null,
                    value: "http://www.w3.org/1999/xhtml"
                )
                .AnyContext();
        }

        // Add xmlns:image attribute if there are images
        var hasImages = sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.Images is not null);

        if (hasImages)
        {
            await writer
                .WriteAttributeStringAsync(
                    prefix: "xmlns",
                    localName: "image",
                    ns: null,
                    value: "http://www.google.com/schemas/sitemap-image/1.1"
                )
                .AnyContext();
        }

        // write URLs to the sitemap
        foreach (var sitemapUrl in sitemapUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _WriteUrlNodeAsync(writer, sitemapUrl, cancellationToken).AnyContext();
        }

        await writer.WriteEndElementAsync().AnyContext();
    }

    #region Helpers

    private static async Task _WriteUrlNodeAsync(
        XmlWriter writer,
        SitemapUrl sitemapUrl,
        CancellationToken cancellationToken
    )
    {
        var hasAlternates = sitemapUrl.AlternateLocations is not null;

        if (!hasAlternates)
        {
            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null).AnyContext();

            await writer
                .WriteElementStringAsync(
                    prefix: null,
                    localName: "loc",
                    ns: null,
                    value: sitemapUrl.Location!.AbsoluteUri
                )
                .AnyContext();

            if (sitemapUrl.Images is not null)
            {
                await _WriteImagesAsync(writer, sitemapUrl.Images, cancellationToken).AnyContext();
            }

            _WriteOtherNodes(writer, sitemapUrl);
            await writer.WriteEndElementAsync().AnyContext();

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
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null).AnyContext();

            await writer
                .WriteElementStringAsync(prefix: null, localName: "loc", ns: null, value: url.Location.AbsoluteUri)
                .AnyContext();

            // Write images in each alternate URL
            if (sitemapUrl.Images is not null)
            {
                await _WriteImagesAsync(writer, sitemapUrl.Images, cancellationToken).AnyContext();
            }

            // Write alternate URLs
            await _WriteAlternateUrlsReferenceAsync(writer, sitemapUrl.AlternateLocations, cancellationToken)
                .AnyContext();

            // Write properties
            _WriteOtherNodes(writer, sitemapUrl);

            await writer.WriteEndElementAsync().AnyContext();
        }
    }

    private static async Task _WriteAlternateUrlsReferenceAsync(
        XmlWriter writer,
        IEnumerable<SitemapAlternateUrl> alternateUrls,
        CancellationToken cancellationToken
    )
    {
        foreach (var alternate in alternateUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteStartElementAsync(prefix: "xhtml", localName: "link", ns: null).AnyContext();
            await writer
                .WriteAttributeStringAsync(prefix: null, localName: "rel", ns: null, value: "alternate")
                .AnyContext();

            await writer
                .WriteAttributeStringAsync(prefix: null, localName: "hreflang", ns: null, value: alternate.LanguageCode)
                .AnyContext();

            await writer
                .WriteAttributeStringAsync(
                    prefix: null,
                    localName: "href",
                    ns: null,
                    value: alternate.Location.AbsoluteUri
                )
                .AnyContext();

            await writer.WriteEndElementAsync().AnyContext();
        }
    }

    private static async Task _WriteImagesAsync(
        XmlWriter writer,
        IEnumerable<SitemapImage> images,
        CancellationToken cancellationToken
    )
    {
        /*
         * <image:image>
         *     <image:loc>https://example.com/image.jpg</image:loc>
         * </image:image>
         */

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteStartElementAsync(prefix: "image", localName: "image", ns: null).AnyContext();

            await writer
                .WriteElementStringAsync(prefix: "image", localName: "loc", ns: null, value: image.Location.AbsoluteUri)
                .AnyContext();

            await writer.WriteEndElementAsync().AnyContext();
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
