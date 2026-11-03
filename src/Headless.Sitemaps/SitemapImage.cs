// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sitemaps;

/// <summary>Represents an image associated with a sitemap URL node.</summary>
[PublicAPI]
public sealed class SitemapImage
{
    /// <summary>Initializes a new instance of the <see cref="SitemapImage"/> class.</summary>
    /// <param name="location">The absolute URL of the image.</param>
    /// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="location"/> is not an absolute URI.</exception>
    public SitemapImage(Uri location)
    {
        Argument.IsNotNull(location);
        Argument.IsTrue(location.IsAbsoluteUri, SitemapConstants.AbsoluteLocationRequired, nameof(location));
        Location = location;
    }

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
    public Uri Location { get; }
}
