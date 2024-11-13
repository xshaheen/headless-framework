// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

namespace Framework.Sitemaps;

/// <summary>Represents sitemap alternate URL node.</summary>
[PublicAPI]
public sealed record SitemapAlternateUrl
{
    /// <summary>Alternate url.</summary>
    public required Uri Location { get; init; }

    /// <summary>
    /// Language/region codes (in ISO 639-1 format) and optionally
    /// a region (in ISO 3166-1 Alpha 2 format) of an alternate URL
    /// Example ar-eg
    /// </summary>
    /// <remarks>
    /// ISO 639-1: https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes
    /// ISO 3166-1 Alpha 2: https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2
    /// </remarks>
    public required string LanguageCode { get; init; }
}
