// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Represents a node that references a sub-sitemap file in a sitemap index.</summary>
[PublicAPI]
public sealed record SitemapReference
{
    /// <summary>
    /// Gets the location URL of the sitemap file.
    /// </summary>
    public required Uri Location { get; init; }

    /// <summary>
    /// Gets the time that the corresponding sitemap file was modified.
    /// </summary>
    public DateTime? LastModified { get; init; }
}
