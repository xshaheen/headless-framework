// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sitemaps;

/// <summary>Represents a localized alternate URL node within a sitemap URL.</summary>
[PublicAPI]
public sealed record SitemapAlternateUrl
{
    /// <summary>Gets the alternate URL location.</summary>
    public required Uri Location { get; init; }

    /// <summary>
    /// Gets the language code in ISO 639-1 format, with an optional region code in ISO 3166-1 Alpha 2 format,
    /// such as <c>ar-eg</c>.
    /// </summary>
    public required string LanguageCode { get; init; }
}
