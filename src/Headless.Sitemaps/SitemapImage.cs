// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Represents an image associated with a sitemap URL node.</summary>
/// <param name="location">The URL of the image.</param>
[PublicAPI]
public sealed class SitemapImage(Uri location)
{
    /// <summary>
    /// Gets the URL of the image.
    /// </summary>
    public Uri Location { get; } = location;
}
