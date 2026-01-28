// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Represents sitemap URL node.</summary>
[PublicAPI]
public sealed class SitemapUrl
{
    /// <summary>Create a sitemap URL.</summary>
    /// <param name="location"></param>
    /// <param name="lastModified"></param>
    /// <param name="changeFrequency"></param>
    /// <param name="priority"></param>
    public SitemapUrl(
        Uri location,
        DateTime? lastModified = null,
        ChangeFrequency? changeFrequency = null,
        float? priority = null,
        IEnumerable<SitemapImage>? images = null
    )
    {
        Location = location;
        LastModified = lastModified;
        ChangeFrequency = changeFrequency;
        Priority = priority;
        Images = images;
    }

    /// <summary>Create a sitemap URL that with its alternates.</summary>
    /// <param name="alternateLocations"></param>
    /// <param name="lastModified"></param>
    /// <param name="changeFrequency"></param>
    /// <param name="priority"></param>
    public SitemapUrl(
        IEnumerable<SitemapAlternateUrl> alternateLocations,
        DateTime? lastModified = null,
        ChangeFrequency? changeFrequency = null,
        float? priority = null,
        IEnumerable<SitemapImage>? images = null,
        string[]? writeAlternateLanguageCodes = null
    )
    {
        AlternateLocations = alternateLocations;
        LastModified = lastModified;
        ChangeFrequency = changeFrequency;
        Priority = priority;
        Images = images;
        WriteAlternateLanguageCodes = writeAlternateLanguageCodes;
    }

    /// <summary>Gets the full URL of the page.</summary>
    public Uri? Location { get; }

    /// <summary>
    /// The priority of that URL relative to other URLs on the site (0.0 to 1.0).
    /// This allows webmasters to suggest to crawlers which pages are considered more important.
    /// </summary>
    /// <remarks>Currently (2021) Google ignores it.</remarks>
    public float? Priority { get; }

    /// <summary>The date of the last modification of the page.</summary>
    public DateTime? LastModified { get; }

    /// <summary>How frequently the page is likely to change.</summary>
    /// <remarks>Currently (2021) Google ignores it.</remarks>
    public ChangeFrequency? ChangeFrequency { get; }

    /// <summary>
    /// Encloses all information about a single image. Each &lt;url&gt; tag can contain up to 1,000 image tags.
    /// </summary>
    public IEnumerable<SitemapImage>? Images { get; }

    /// <summary>Gets alternate localized URLs of the page.</summary>
    public IEnumerable<SitemapAlternateUrl>? AlternateLocations { get; }

    /// <summary>Forces writing alternate URLs only for the specified language/region codes. null means all.</summary>
    public string[]? WriteAlternateLanguageCodes { get; set; }
}
