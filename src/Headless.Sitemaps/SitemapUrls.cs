// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;
using Headless.Checks;

namespace Headless.Sitemaps;

/// <summary>
/// Sitemap file builder.
/// </summary>
/// <remarks>https://developers.google.com/search/docs/advanced/sitemaps/build-sitemap</remarks>
[PublicAPI]
public static class SitemapUrls
{
    /// <summary>
    /// Generates sitemap files of at most <see cref="SitemapConstants.MaxSitemapUrls"/> URLs each and hands
    /// every one to <paramref name="onShard"/> as soon as it fills, so memory holds at most one shard's URL
    /// references and no shard XML unless the callback buffers it.
    /// </summary>
    /// <param name="sitemapUrls">The sitemap URLs to write, enumerated once.</param>
    /// <param name="onShard">
    /// Called once per shard, in order. Write the shard with <see cref="SitemapShard.WriteToAsync"/> before the
    /// returned task completes: the shard's URL buffer is reused for the next shard after that.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation; it is also passed to <paramref name="onShard"/>.</param>
    /// <returns>The number of shards written, for building the sitemap index. <c>0</c> for an empty input.</returns>
    /// <remarks>An exception thrown by <paramref name="onShard"/> stops enumeration and propagates.</remarks>
    public static async Task<int> WriteAsync(
        this IEnumerable<SitemapUrl> sitemapUrls,
        Func<SitemapShard, CancellationToken, ValueTask> onShard,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sitemapUrls);
        Argument.IsNotNull(onShard);

        var capacity = sitemapUrls.TryGetNonEnumeratedCount(out var count)
            ? Math.Min(count, SitemapConstants.MaxSitemapUrls)
            : 0;

        var sharder = new Sharder(onShard, capacity);

        foreach (var url in sitemapUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sharder.AddAsync(url, cancellationToken).ConfigureAwait(false);
        }

        return await sharder.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates sitemap files of at most <see cref="SitemapConstants.MaxSitemapUrls"/> URLs each from an
    /// asynchronous source, such as a streamed database query, and hands every one to <paramref name="onShard"/>
    /// as soon as it fills, so memory holds at most one shard's URL references.
    /// </summary>
    /// <param name="sitemapUrls">The sitemap URLs to write, enumerated once.</param>
    /// <param name="onShard">
    /// Called once per shard, in order. Write the shard with <see cref="SitemapShard.WriteToAsync"/> before the
    /// returned task completes: the shard's URL buffer is reused for the next shard after that.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to cancel the operation; it is passed to the source enumerator and to <paramref name="onShard"/>.
    /// </param>
    /// <returns>The number of shards written, for building the sitemap index. <c>0</c> for an empty input.</returns>
    /// <remarks>An exception thrown by <paramref name="onShard"/> stops enumeration and propagates.</remarks>
    public static async Task<int> WriteAsync(
        this IAsyncEnumerable<SitemapUrl> sitemapUrls,
        Func<SitemapShard, CancellationToken, ValueTask> onShard,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sitemapUrls);
        Argument.IsNotNull(onShard);

        var sharder = new Sharder(onShard, capacity: 0);

        await foreach (var url in sitemapUrls.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sharder.AddAsync(url, cancellationToken).ConfigureAwait(false);
        }

        return await sharder.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a sitemap file into the stream.</summary>
    /// <param name="sitemapUrls">The collection of sitemap URLs to write.</param>
    /// <param name="output">The output stream receiving the XML content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the sitemap has been written to the stream.</returns>
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

    // Holds the sharding state both WriteAsync overloads share, so the sync and async sources differ only in
    // how they enumerate. One List is reused across shards: that is what bounds memory to one shard.
    private sealed class Sharder(Func<SitemapShard, CancellationToken, ValueTask> onShard, int capacity)
    {
        private readonly List<SitemapUrl> _buffer = new(capacity);
        private int _shardCount;

        public ValueTask AddAsync(SitemapUrl url, CancellationToken cancellationToken)
        {
            _buffer.Add(url);

            return _buffer.Count == SitemapConstants.MaxSitemapUrls
                ? _FlushAsync(cancellationToken)
                : ValueTask.CompletedTask;
        }

        public async ValueTask<int> CompleteAsync(CancellationToken cancellationToken)
        {
            if (_buffer.Count > 0)
            {
                await _FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return _shardCount;
        }

        private async ValueTask _FlushAsync(CancellationToken cancellationToken)
        {
            var shard = new SitemapShard(_shardCount, _buffer);

            try
            {
                await onShard(shard, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                shard.Invalidate();
            }

            _shardCount++;
            _buffer.Clear();
        }
    }

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
    private static string _ToChangeFreqValue(ChangeFrequency frequency)
    {
        return frequency switch
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
    }

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
