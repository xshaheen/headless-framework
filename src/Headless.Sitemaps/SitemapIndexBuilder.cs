// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;
using Headless.Checks;

namespace Headless.Sitemaps;

/// <summary>
/// Sitemap index file builder.
/// </summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/large-sitemaps</remarks>
[PublicAPI]
public static class SitemapIndexBuilder
{
    /// <summary>Writes a sitemap index file into the stream.</summary>
    /// <param name="sitemapReferences">The collection of sitemap references to include in the index.</param>
    /// <param name="output">The output stream receiving the XML content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the index has been written to the stream.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="sitemapReferences"/> holds more than <see cref="SitemapConstants.MaxSitemapIndexEntries"/>
    /// references, or its index XML would exceed <see cref="SitemapConstants.MaxSitemapBytes"/> bytes. Nothing is
    /// written to <paramref name="output"/>. An index cannot reference another index, so split the sitemaps across
    /// several indexes and submit each one.
    /// </exception>
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

        Argument.IsNotNull(sitemapReferences);
        Argument.IsNotNull(output);

        // Validate before writing, so a rejected index leaves no partial file behind. Taking one past the limit is
        // enough to detect the overflow without buffering an unbounded source.
        var references = sitemapReferences.Take(SitemapConstants.MaxSitemapIndexEntries + 1).ToList();

        Argument.IsTrue(
            references.Count <= SitemapConstants.MaxSitemapIndexEntries,
            $"A sitemap index holds at most {SitemapConstants.MaxSitemapIndexEntries} sitemaps; split them across several indexes.",
            nameof(sitemapReferences)
        );

        var size = await _MeasureAsync(references, cancellationToken).ConfigureAwait(false);

        Argument.IsTrue(
            size <= SitemapConstants.MaxSitemapBytes,
            $"A sitemap index file holds at most {SitemapConstants.MaxSitemapBytes} bytes, but these references write {size}; split them across several indexes.",
            nameof(sitemapReferences)
        );

        await using var writer = XmlWriter.Create(output, SitemapConstants.WriterSettings);
        await _WriteSitemapIndexStartAsync(writer).ConfigureAwait(false);

        // Write sitemaps URL.
        foreach (var sitemapReference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _WriteSitemapRefNodeAsync(writer, sitemapReference).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    // The index's UTF-8 size as written, measured without buffering the XML.
    private static async Task<long> _MeasureAsync(
        List<SitemapReference> references,
        CancellationToken cancellationToken
    )
    {
        if (references.Count == 0)
        {
            return 0;
        }

        await using var sizer = await SitemapEntrySizer<SitemapReference>
            .CreateAsync(
                _WriteSitemapIndexStartAsync,
                static (writer, reference, _) => _WriteSitemapRefNodeAsync(writer, reference),
                references[0],
                cancellationToken
            )
            .ConfigureAwait(false);

        var size = sizer.DocumentOverhead;

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            size += await sizer.MeasureAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        return size;
    }

    private static async Task _WriteSitemapIndexStartAsync(XmlWriter writer)
    {
        await writer.WriteStartDocumentAsync().ConfigureAwait(false);

        await writer
            .WriteStartElementAsync(
                prefix: null,
                localName: "sitemapindex",
                ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
            )
            .ConfigureAwait(false);
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
