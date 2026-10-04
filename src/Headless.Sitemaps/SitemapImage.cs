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
    /// <remarks>
    /// In some cases, the image URL may not be on the same domain as your main site. This is fine
    /// as long as you verify both domains in Search Console. For example, when you use a content
    /// delivery network such as Google Sites to host your images, make sure that the hosting site
    /// is verified in Search Console. In addition, make sure that your robots.txt file does not
    /// disallow the crawling of any content you want indexed.
    /// </remarks>
    public Uri Location { get; } = location;
}
