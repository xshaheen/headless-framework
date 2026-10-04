// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Represents a node that references a sub-sitemap file in a sitemap index.</summary>
[PublicAPI]
public sealed record SitemapReference
{
    /// <summary>
    /// Identifies the location of the sitemap. This location can be a sitemap, an Atom file,
    /// an RSS file, or a simple text file.
    /// </summary>
    public required Uri Location { get; init; }

    /// <summary>
    /// Identifies the time that the corresponding sitemap file was modified. It does not correspond
    /// to the time that any of the pages listed in that sitemap were changed.
    /// </summary>
    /// <remarks>
    /// By providing the last modification timestamp, you enable search engine crawlers to retrieve
    /// only a subset of the sitemaps in the index — a crawler may only retrieve sitemaps that were
    /// modified since a certain date. This incremental sitemap fetching mechanism allows for the
    /// rapid discovery of new URLs on large sites.
    /// </remarks>
    public DateTime? LastModified { get; init; }
}
