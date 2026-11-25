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
    /// Generates sitemap files of at most <see cref="SitemapConstants.MaxSitemapUrls"/> URLs and
    /// <see cref="SitemapConstants.MaxSitemapBytes"/> bytes each and hands every one to <paramref name="onShard"/>
    /// as soon as it fills, so memory holds at most one shard's URL references and no shard XML unless the
    /// callback buffers it.
    /// </summary>
    /// <param name="sitemapUrls">The sitemap URLs to write, enumerated once.</param>
    /// <param name="onShard">
    /// Called once per shard, in order. Write the shard with <see cref="SitemapShard.WriteToAsync"/> before the
    /// returned task completes: the shard's URL buffer is reused for the next shard after that.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation; it is also passed to <paramref name="onShard"/>.</param>
    /// <returns>The number of shards written, for building the sitemap index. <c>0</c> for an empty input.</returns>
    /// <remarks>
    /// An exception thrown by <paramref name="onShard"/> stops enumeration and propagates. The byte size is the
    /// UTF-8 XML as <see cref="SitemapShard.WriteToAsync"/> writes it. One <see cref="SitemapUrl"/> whose own XML
    /// passes a limit, such as one with more alternates than <see cref="SitemapConstants.MaxSitemapUrls"/>, gets a
    /// shard of its own that exceeds it, because its entries cannot be split across files.
    /// </remarks>
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

        await using var sharder = new Sharder(onShard, capacity);

        foreach (var url in sitemapUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sharder.AddAsync(url, cancellationToken).ConfigureAwait(false);
        }

        return await sharder.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates sitemap files of at most <see cref="SitemapConstants.MaxSitemapUrls"/> URLs and
    /// <see cref="SitemapConstants.MaxSitemapBytes"/> bytes each from an asynchronous source, such as a streamed
    /// database query, and hands every one to <paramref name="onShard"/> as soon as it fills, so memory holds at
    /// most one shard's URL references.
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
    /// <remarks>
    /// An exception thrown by <paramref name="onShard"/> stops enumeration and propagates. The byte size is the
    /// UTF-8 XML as <see cref="SitemapShard.WriteToAsync"/> writes it. One <see cref="SitemapUrl"/> whose own XML
    /// passes a limit, such as one with more alternates than <see cref="SitemapConstants.MaxSitemapUrls"/>, gets a
    /// shard of its own that exceeds it, because its entries cannot be split across files.
    /// </remarks>
    public static async Task<int> WriteAsync(
        this IAsyncEnumerable<SitemapUrl> sitemapUrls,
        Func<SitemapShard, CancellationToken, ValueTask> onShard,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(sitemapUrls);
        Argument.IsNotNull(onShard);

        await using var sharder = new Sharder(onShard, capacity: 0);

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

        // Declare xmlns:xhtml and xmlns:image only when a URL uses them.
        await _WriteUrlSetStartAsync(
                writer,
                hasAlternateUrls: sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.AlternateLocations is not null),
                hasImages: sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.Images is not null)
            )
            .ConfigureAwait(false);

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
        : IAsyncDisposable
    {
        private readonly List<SitemapUrl> _buffer = new(capacity);
        private SitemapEntrySizer<SitemapUrl>? _sizer;
        private int _shardCount;

        // The limit is on <url> elements, not SitemapUrl entries: an entry with alternates writes one <url> per
        // emitted alternate.
        private int _urlNodeCount;

        // The bytes the buffered entries add to the file, measured as written. Long URLs, alternates, and images
        // pass the byte limit well before the URL limit, so a count-only cut writes files crawlers reject.
        private long _entryBytes;

        public async ValueTask AddAsync(SitemapUrl url, CancellationToken cancellationToken)
        {
            var urlNodes = _CountUrlNodes(url);

            _sizer ??= await SitemapEntrySizer<SitemapUrl>
                .CreateAsync(
                    static writer => _WriteUrlSetStartAsync(writer, hasAlternateUrls: true, hasImages: true),
                    _WriteUrlNodeAsync,
                    url,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var entryBytes = await _sizer.MeasureAsync(url, cancellationToken).ConfigureAwait(false);

            // Close the shard before an entry that would overflow it. An entry that alone exceeds a limit still
            // gets a shard of its own, because one entry's alternates cannot be split across files.
            if (
                _buffer.Count > 0
                && (
                    _urlNodeCount + urlNodes > SitemapConstants.MaxSitemapUrls
                    || _sizer.DocumentOverhead + _entryBytes + entryBytes > SitemapConstants.MaxSitemapBytes
                )
            )
            {
                await _FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            _buffer.Add(url);
            _urlNodeCount += urlNodes;
            _entryBytes += entryBytes;

            if (_urlNodeCount >= SitemapConstants.MaxSitemapUrls)
            {
                await _FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask<int> CompleteAsync(CancellationToken cancellationToken)
        {
            if (_buffer.Count > 0)
            {
                await _FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return _shardCount;
        }

        public ValueTask DisposeAsync()
        {
            return _sizer?.DisposeAsync() ?? ValueTask.CompletedTask;
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
            _urlNodeCount = 0;
            _entryBytes = 0;
        }
    }

    private static async Task _WriteUrlSetStartAsync(XmlWriter writer, bool hasAlternateUrls, bool hasImages)
    {
        await writer.WriteStartDocumentAsync().ConfigureAwait(false);

        await writer
            .WriteStartElementAsync(
                prefix: null,
                localName: "urlset",
                ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
            )
            .ConfigureAwait(false);

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
    }

    private static int _CountUrlNodes(SitemapUrl sitemapUrl)
    {
        return sitemapUrl.AlternateLocations is null ? 1 : _GetEmittedAlternates(sitemapUrl).Count();
    }

    // The alternates that get their own <url> entry. Shared by the writer and the shard counter so the count a
    // shard is cut on always matches what the writer emits.
    private static IEnumerable<SitemapAlternateUrl> _GetEmittedAlternates(SitemapUrl sitemapUrl)
    {
        var alternateLocations = sitemapUrl.AlternateLocations ?? [];
        var languageCodes = sitemapUrl.WriteAlternateLanguageCodes;

        return languageCodes is null
            ? alternateLocations
            : alternateLocations.Where(alt =>
                languageCodes.Contains(alt.LanguageCode, StringComparer.OrdinalIgnoreCase)
            );
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
        foreach (var url in _GetEmittedAlternates(sitemapUrl))
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
