// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Represents how frequently the content at a sitemap URL is likely to change.</summary>
/// <remarks>
/// The value is a hint to crawlers and may not affect actual crawl rate. Use <see cref="Always"/> for pages
/// that change with every access, and <see cref="Never"/> for archived content that will not change.
/// </remarks>
[PublicAPI]
public enum ChangeFrequency
{
    /// <summary>The page changes with every access (for example, dynamically generated pages).</summary>
    Always,

    /// <summary>The page changes approximately every hour.</summary>
    Hourly,

    /// <summary>The page changes approximately once per day.</summary>
    Daily,

    /// <summary>The page changes approximately once per week.</summary>
    Weekly,

    /// <summary>The page changes approximately once per month.</summary>
    Monthly,

    /// <summary>The page changes approximately once per year.</summary>
    Yearly,

    /// <summary>The page is archived and will not change. Search engines may still recrawl it periodically.</summary>
    Never,
}
