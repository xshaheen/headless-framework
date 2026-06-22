// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sitemaps;

/// <summary>Represents sitemap URL node.</summary>
[PublicAPI]
public sealed class SitemapUrl
{
    /// <summary>Create a sitemap URL.</summary>
    /// <param name="location">The full URL of the page.</param>
    /// <param name="lastModified">The date of the last modification of the page.</param>
    /// <param name="changeFrequency">How frequently the page is likely to change.</param>
    /// <param name="priority">The priority of this URL relative to other URLs on the site (0.0 to 1.0).</param>
    /// <param name="images">The images associated with this URL. Each URL can contain up to 1,000 image tags.</param>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="priority"/> is not between 0.0 and 1.0.</exception>
    public SitemapUrl(
        Uri location,
        DateTime? lastModified = null,
        ChangeFrequency? changeFrequency = null,
        float? priority = null,
        IEnumerable<SitemapImage>? images = null
    )
    {
        Argument.IsNotNull(location);

        if (priority is not null)
        {
            Argument.IsInclusiveBetween(priority.Value, 0f, 1f);
        }

        Location = location;
        LastModified = lastModified;
        ChangeFrequency = changeFrequency;
        Priority = priority;
        Images = images?.ToArray();
    }

    /// <summary>Create a sitemap URL with its localized alternates.</summary>
    /// <param name="alternateLocations">The alternate localized URLs of the page.</param>
    /// <param name="lastModified">The date of the last modification of the page.</param>
    /// <param name="changeFrequency">How frequently the page is likely to change.</param>
    /// <param name="priority">The priority of this URL relative to other URLs on the site (0.0 to 1.0).</param>
    /// <param name="images">The images associated with this URL. Each URL can contain up to 1,000 image tags.</param>
    /// <param name="writeAlternateLanguageCodes">
    /// Restricts which localized versions get their own &lt;url&gt; entry to the specified language/region codes;
    /// every emitted entry still references all alternates (per hreflang reciprocity). <see langword="null"/> means all.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="alternateLocations"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="priority"/> is not between 0.0 and 1.0.</exception>
    public SitemapUrl(
        IEnumerable<SitemapAlternateUrl> alternateLocations,
        DateTime? lastModified = null,
        ChangeFrequency? changeFrequency = null,
        float? priority = null,
        IEnumerable<SitemapImage>? images = null,
        string[]? writeAlternateLanguageCodes = null
    )
    {
        Argument.IsNotNull(alternateLocations);

        if (priority is not null)
        {
            Argument.IsInclusiveBetween(priority.Value, 0f, 1f);
        }

        AlternateLocations = alternateLocations.ToArray();
        LastModified = lastModified;
        ChangeFrequency = changeFrequency;
        Priority = priority;
        Images = images?.ToArray();
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

    /// <summary>
    /// Restricts which localized versions get their own &lt;url&gt; entry to the specified language/region codes;
    /// every emitted entry still references all alternates. <see langword="null"/> means all.
    /// </summary>
    public string[]? WriteAlternateLanguageCodes { get; }
}
