// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Sitemaps;

public sealed class SitemapImage(Uri location)
{
    /// <summary>
    /// The URL of the image.
    /// In some cases, the image URL may not be on the same domain as your main site.
    /// This is fine, as long as you verify both domains in Search Console.
    /// If, for example, you use a content delivery network such as Google Sites to host your images,
    /// make sure that the hosting site is verified in Search Console. In addition, make sure that
    /// your robots.txt file doesn't disallow the crawling of any content you want indexed.
    /// </summary>
    public Uri Location { get; } = location;
}
