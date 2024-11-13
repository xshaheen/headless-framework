// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

namespace Framework.Sitemaps;

/// <summary>Represent a node that references a sub-sitemap.</summary>
[PublicAPI]
public sealed record SitemapReference
{
    /// <summary>
    /// Identifies the location of the Sitemap.
    /// This location can be a Sitemap, an Atom file, RSS file or a simple text file.
    /// </summary>
    public required Uri Location { get; init; }

    /// <summary>
    /// Identifies the time that the corresponding Sitemap file was modified.
    /// It does not correspond to the time that any of the pages listed in that Sitemap were changed.
    /// By providing the last modification timestamp, you enable search engine crawlers to
    /// retrieve only a subset of the Sitemaps in the index that is a crawler may only retrieve
    /// Sitemaps that were modified since a certain date. This incremental Sitemap fetching
    /// mechanism allows for the rapid discovery of new URLs on large sites.
    /// </summary>
    public DateTime? LastModified { get; init; }
}
