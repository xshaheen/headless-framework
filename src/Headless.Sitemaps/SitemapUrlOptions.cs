// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Specifies optional metadata for a sitemap URL.</summary>
[PublicAPI]
public sealed class SitemapUrlOptions
{
    /// <summary>Creates an empty set of optional sitemap URL metadata.</summary>
    public SitemapUrlOptions() { }

    /// <summary>Gets the date of the page's last modification.</summary>
    public DateTime? LastModified { get; init; }

    /// <summary>Gets how frequently the page is likely to change.</summary>
    public ChangeFrequency? ChangeFrequency { get; init; }

    /// <summary>Gets the URL priority relative to other URLs on the site, from 0.0 through 1.0.</summary>
    public float? Priority { get; init; }

    /// <summary>Gets the images associated with the URL.</summary>
    public IEnumerable<SitemapImage>? Images { get; init; }

    /// <summary>
    /// Gets the language or language-region codes whose alternate locations receive their own URL entry.
    /// <see langword="null"/> includes all alternates.
    /// </summary>
    public IEnumerable<string>? WriteAlternateLanguageCodes { get; init; }
}
