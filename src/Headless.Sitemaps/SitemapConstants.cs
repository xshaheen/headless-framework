// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml;

namespace Headless.Sitemaps;

/// <summary>Provides shared constants for sitemap generation.</summary>
[PublicAPI]
public static class SitemapConstants
{
    /// <summary>Gets the date format string used in sitemaps.</summary>
    public const string SitemapDateFormat = "yyyy-MM-dd";

    /// <summary>Gets the maximum number of URLs allowed in a single sitemap file.</summary>
    public const int MaxSitemapUrls = 50_000;

    internal static readonly XmlWriterSettings WriterSettings = new()
    {
        Async = true,
        Indent = true,
        Encoding = StringHelper.Utf8WithoutBom,
        NewLineChars = "\n",
    };
}
