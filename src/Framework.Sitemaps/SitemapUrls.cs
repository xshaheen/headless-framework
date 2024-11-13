// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

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
        await using var writer = XmlWriter.Create(output, SitemapConstants.WriterSettings);
        await writer.WriteStartDocumentAsync();

        await writer.WriteStartElementAsync(
            prefix: null,
            localName: "urlset",
            ns: "http://www.sitemaps.org/schemas/sitemap/0.9"
        );

        if (sitemapUrls.Any(predicate: sitemapUrl => sitemapUrl.AlternateLocations is not null))
        {
            await writer.WriteAttributeStringAsync(
                prefix: "xmlns",
                localName: "xhtml",
                ns: null,
                value: "http://www.w3.org/1999/xhtml"
            );
        }

        // write URLs to the sitemap
        foreach (var sitemapUrl in sitemapUrls)
        {
            await _WriteUrlNodeAsync(writer: writer, sitemapUrl: sitemapUrl);
        }

        await writer.WriteEndElementAsync();
    }

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
            _WriteOtherNodes(writer, sitemapUrl);
            await writer.WriteEndElementAsync();

            return;
        }

        // write with alternates

        foreach (var url in sitemapUrl.AlternateLocations!)
        {
            await writer.WriteStartElementAsync(prefix: null, localName: "url", ns: null);
            await writer.WriteElementStringAsync(
                prefix: null,
                localName: "loc",
                ns: null,
                value: url.Location.AbsoluteUri
            );
            await _WriteAlternateUrlsReferenceAsync(writer, sitemapUrl.AlternateLocations);
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
}
