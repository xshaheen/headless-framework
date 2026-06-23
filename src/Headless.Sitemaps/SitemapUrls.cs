// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Xml;

namespace Headless.Sitemaps;

/// <summary>Sitemap file builder</summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/build-sitemap</remarks>
[PublicAPI]
public static class SitemapUrls
{
    /// <summary>
    /// Generate one or more sitemap files, splitting into separate files when the URL count exceeds
    /// <see cref="SitemapConstants.MaxSitemapUrls"/>. Each returned stream is rewound to its start
    /// (<see cref="Stream.Position"/> is <c>0</c>) and is owned by the caller, who must dispose it.
    /// </summary>
    /// <remarks>
    /// This eagerly buffers every shard in memory. For very large sites prefer <see cref="WriteEachAsync"/>,
    /// which yields shards lazily so the caller can flush and dispose each one before the next is built.
    /// </remarks>
    public static async Task<IReadOnlyList<Stream>> WriteAsync(
        this IReadOnlyCollection<SitemapUrl> sitemapUrls,
        CancellationToken cancellationToken = default
    )
    {
        var streams = new List<Stream>();

        await foreach (var stream in sitemapUrls.WriteEachAsync(cancellationToken).ConfigureAwait(false))
        {
            streams.Add(stream);
        }

        return streams;
    }

    /// <summary>
    /// Lazily generate sitemap files, splitting into separate files when the URL count exceeds
    /// <see cref="SitemapConstants.MaxSitemapUrls"/>. Each yielded stream is rewound to its start
    /// (<see cref="Stream.Position"/> is <c>0</c>); the caller owns it and should dispose it before requesting the next.
    /// </summary>
    public static async IAsyncEnumerable<MemoryStream> WriteEachAsync(
        this IReadOnlyCollection<SitemapUrl> sitemapUrls,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        // split URLs into separate lists based on the max size
        var sitemaps = sitemapUrls
            .Select((url, index) => new { Index = index, Value = url })
            .GroupBy(group => group.Index / SitemapConstants.MaxSitemapUrls)
            .Select(group => group.Select(url => url.Value).ToArray());

        foreach (var sitemap in sitemaps)
        {
            var stream = new MemoryStream();
            await sitemap.WriteToAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            yield return stream;
        }
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
        await writer.WriteStartDocumentAsync().ConfigureAwait(false);

        await writer
            .WriteStartElementAsync(
                prefix: null,
                localName: "urlset",
                ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
            )
            .ConfigureAwait(false);

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
                .ConfigureAwait(false);
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
                .ConfigureAwait(false);
        }

        // write URLs to the sitemap
        foreach (var sitemapUrl in sitemapUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _WriteUrlNodeAsync(writer, sitemapUrl, cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    #region Helpers

    private static async Task _WriteUrlNodeAsync(
        XmlWriter writer,
        SitemapUrl sitemapUrl,
        CancellationToken cancellationToken
    )
    {
        var alternateLocations = sitemapUrl.AlternateLocations;

        if (alternateLocations is null)
        {
            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null).ConfigureAwait(false);

            // Location is non-null here: the alternates constructor is the only one that leaves it null, and it
            // always sets AlternateLocations. A null AlternateLocations therefore implies the simple constructor,
            // which validates Location is non-null.
            await writer
                .WriteElementStringAsync(
                    prefix: null,
                    localName: "loc",
                    ns: null,
                    value: sitemapUrl.Location!.AbsoluteUri
                )
                .ConfigureAwait(false);

            if (sitemapUrl.Images is not null)
            {
                await _WriteImagesAsync(writer, sitemapUrl.Images, cancellationToken).ConfigureAwait(false);
            }

            await _WriteOtherNodesAsync(writer, sitemapUrl).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);

            return;
        }

        // write with alternates
        var filteredAlternates = sitemapUrl.WriteAlternateLanguageCodes is null
            ? alternateLocations
            : alternateLocations.Where(predicate: alt =>
                sitemapUrl.WriteAlternateLanguageCodes!.Contains(alt.LanguageCode, StringComparer.OrdinalIgnoreCase)
            );

        foreach (var url in filteredAlternates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null).ConfigureAwait(false);

            await writer
                .WriteElementStringAsync(prefix: null, localName: "loc", ns: null, value: url.Location.AbsoluteUri)
                .ConfigureAwait(false);

            // Write images in each alternate URL
            if (sitemapUrl.Images is not null)
            {
                await _WriteImagesAsync(writer, sitemapUrl.Images, cancellationToken).ConfigureAwait(false);
            }

            // Write alternate URLs
            await _WriteAlternateUrlsReferenceAsync(writer, alternateLocations, cancellationToken)
                .ConfigureAwait(false);

            // Write properties
            await _WriteOtherNodesAsync(writer, sitemapUrl).ConfigureAwait(false);

            await writer.WriteEndElementAsync().ConfigureAwait(false);
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

            await writer.WriteStartElementAsync(prefix: "xhtml", localName: "link", ns: null).ConfigureAwait(false);
            await writer
                .WriteAttributeStringAsync(prefix: null, localName: "rel", ns: null, value: "alternate")
                .ConfigureAwait(false);

            await writer
                .WriteAttributeStringAsync(prefix: null, localName: "hreflang", ns: null, value: alternate.LanguageCode)
                .ConfigureAwait(false);

            await writer
                .WriteAttributeStringAsync(
                    prefix: null,
                    localName: "href",
                    ns: null,
                    value: alternate.Location.AbsoluteUri
                )
                .ConfigureAwait(false);

            await writer.WriteEndElementAsync().ConfigureAwait(false);
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

            await writer.WriteStartElementAsync(prefix: "image", localName: "image", ns: null).ConfigureAwait(false);

            await writer
                .WriteElementStringAsync(prefix: "image", localName: "loc", ns: null, value: image.Location.AbsoluteUri)
                .ConfigureAwait(false);

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }
    }

    // Returns the interned lowercase sitemap token (no allocation), versus ToString().ToLowerInvariant()
    // which allocates a new lowercased string per URL.
    private static string _ToChangeFreqValue(ChangeFrequency frequency) =>
        frequency switch
        {
            ChangeFrequency.Always => "always",
            ChangeFrequency.Hourly => "hourly",
            ChangeFrequency.Daily => "daily",
            ChangeFrequency.Weekly => "weekly",
            ChangeFrequency.Monthly => "monthly",
            ChangeFrequency.Yearly => "yearly",
            ChangeFrequency.Never => "never",
            _ => frequency.ToString().ToLowerInvariant(),
        };

    private static async Task _WriteOtherNodesAsync(XmlWriter writer, SitemapUrl sitemapUrl)
    {
        if (sitemapUrl.Priority is not null)
        {
            var value = sitemapUrl.Priority.Value.ToString(format: "N1", provider: CultureInfo.InvariantCulture);
            await writer
                .WriteElementStringAsync(prefix: null, localName: "priority", ns: null, value)
                .ConfigureAwait(false);
        }

        if (sitemapUrl.ChangeFrequency is not null)
        {
            var value = _ToChangeFreqValue(sitemapUrl.ChangeFrequency.Value);
            await writer
                .WriteElementStringAsync(prefix: null, localName: "changefreq", ns: null, value)
                .ConfigureAwait(false);
        }

        if (sitemapUrl.LastModified is not null)
        {
            var value = sitemapUrl.LastModified.Value.ToString(
                format: SitemapConstants.SitemapDateFormat,
                provider: CultureInfo.InvariantCulture
            );

            await writer
                .WriteElementStringAsync(prefix: null, localName: "lastmod", ns: null, value)
                .ConfigureAwait(false);
        }
    }

    #endregion
}
