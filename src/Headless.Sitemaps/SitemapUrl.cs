// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sitemaps;

/// <summary>Represents a sitemap URL node.</summary>
[PublicAPI]
public sealed class SitemapUrl
{
    /// <summary>Initializes a new instance of the <see cref="SitemapUrl"/> class with a single URL location.</summary>
    /// <param name="location">The full URL of the page.</param>
    /// <param name="options">Optional metadata including last modified time, change frequency, priority, and images.</param>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SitemapUrlOptions.Priority"/> is not between 0.0 and 1.0.</exception>
    public SitemapUrl(Uri location, SitemapUrlOptions? options = null)
    {
        Argument.IsNotNull(location);

        if (options?.Priority is not null)
        {
            Argument.IsInclusiveBetween(options.Priority.Value, 0f, 1f);
        }

        Location = location;
        LastModified = options?.LastModified;
        ChangeFrequency = options?.ChangeFrequency;
        Priority = options?.Priority;
        Images = options?.Images?.ToArray();
        WriteAlternateLanguageCodes = options?.WriteAlternateLanguageCodes?.ToArray();
    }

    /// <summary>Initializes a new instance of the <see cref="SitemapUrl"/> class with localized alternate locations.</summary>
    /// <param name="alternateLocations">The alternate localized URLs of the page.</param>
    /// <param name="options">Optional metadata including last modified time, change frequency, priority, images, and language codes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="alternateLocations"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SitemapUrlOptions.Priority"/> is not between 0.0 and 1.0.</exception>
    public SitemapUrl(IEnumerable<SitemapAlternateUrl> alternateLocations, SitemapUrlOptions? options = null)
    {
        Argument.IsNotNull(alternateLocations);

        if (options?.Priority is not null)
        {
            Argument.IsInclusiveBetween(options.Priority.Value, 0f, 1f);
        }

        AlternateLocations = alternateLocations.ToArray();
        LastModified = options?.LastModified;
        ChangeFrequency = options?.ChangeFrequency;
        Priority = options?.Priority;
        Images = options?.Images?.ToArray();
        WriteAlternateLanguageCodes = options?.WriteAlternateLanguageCodes?.ToArray();
    }

    /// <summary>Gets the full URL of the page.</summary>
    public Uri? Location { get; }

    /// <summary>
    /// Gets the priority of that URL relative to other URLs on the site, from 0.0 through 1.0.
    /// This allows webmasters to suggest to crawlers which pages are considered more important.
    /// </summary>
    /// <remarks>Currently (2021) Google ignores it.</remarks>
    public float? Priority { get; }

    /// <summary>Gets the date of the last modification of the page.</summary>
    public DateTime? LastModified { get; }

    /// <summary>
    /// Gets how frequently the page is likely to change.
    /// </summary>
    /// <remarks>Currently (2021) Google ignores it.</remarks>
    public ChangeFrequency? ChangeFrequency { get; }

    /// <summary>
    /// Gets the collection of images associated with the URL. Each <c>&lt;url&gt;</c> entry can
    /// contain up to 1,000 image tags.
    /// </summary>
    public IEnumerable<SitemapImage>? Images { get; }

    /// <summary>Gets alternate localized URLs of the page.</summary>
    public IReadOnlyList<SitemapAlternateUrl>? AlternateLocations { get; }

    /// <summary>
    /// Restricts which localized versions get their own <c>&lt;url&gt;</c> entry to the specified
    /// language/region codes; every emitted entry still references all alternates.
    /// <see langword="null"/> means all.
    /// </summary>
    public string[]? WriteAlternateLanguageCodes { get; }
}
