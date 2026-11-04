// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;

namespace Headless.Sitemaps;

/// <summary>Provides shared constants for sitemap generation.</summary>
[PublicAPI]
public static class SitemapConstants
{
    /// <summary>Gets the date format string used in sitemaps.</summary>
    public const string SitemapDateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Max <c>&lt;url&gt;</c> entries in a single sitemap file, according to Google. A <see cref="SitemapUrl"/> with
    /// alternates counts once per alternate it writes.
    /// </summary>
    public const int MaxSitemapUrls = 50_000;

    // Sitemap readers resolve nothing against a base URL, and Uri.AbsoluteUri throws on a relative Uri only once
    // the writer reaches it, after earlier entries are already in the output stream.
    internal const string AbsoluteLocationRequired = "A sitemap location must be an absolute URI.";

    internal static readonly XmlWriterSettings WriterSettings = new()
    {
        Async = true,
        Indent = true,
        Encoding = StringHelper.Utf8WithoutBom,
        NewLineChars = "\n",
    };
}
