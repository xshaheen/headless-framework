# Headless.Sitemaps

XML sitemap generation utilities for SEO.

## Problem Solved

Provides builders and models for generating XML sitemaps and sitemap indexes compliant with the sitemap protocol, supporting localized URLs, images, change frequency, and priority metadata.

## Key Features

- `SitemapUrl` - URL entry with metadata (lastmod, changefreq, priority)
- `SitemapUrls` - Extension methods to write sitemap URLs to streams
- `SitemapIndexBuilder` - Sitemap index generation for large sites
- `SitemapAlternateUrl` - Localized/alternate URL support (hreflang)
- `SitemapImage` - Image sitemap support
- `ChangeFrequency` - Standard frequency values (always, hourly, daily, weekly, etc.)

## Installation

```bash
dotnet add package Headless.Sitemaps
```

## Usage

### Basic Sitemap

```csharp
var urls = new List<SitemapUrl>
{
    new(
        location: new Uri("https://example.com/"),
        lastModified: DateTime.UtcNow,
        changeFrequency: ChangeFrequency.Daily,
        priority: 1.0f
    ),
    new(
        location: new Uri("https://example.com/about"),
        changeFrequency: ChangeFrequency.Monthly,
        priority: 0.8f
    ),
};

// Write to stream
await using var stream = new MemoryStream();
await urls.WriteToAsync(stream);

// Or auto-split at 50,000 URLs per sitemap
var streams = await urls.WriteAsync();
```

### Localized URLs

```csharp
var urls = new List<SitemapUrl>
{
    new(
        alternateLocations:
        [
            new() { Location = new Uri("https://example.com/en/page"), LanguageCode = "en" },
            new() { Location = new Uri("https://example.com/ar/page"), LanguageCode = "ar" },
        ],
        lastModified: DateTime.UtcNow
    ),
};

await using var stream = new MemoryStream();
await urls.WriteToAsync(stream);
```

### Sitemap Index

```csharp
var references = new List<SitemapReference>
{
    new() { Location = new Uri("https://example.com/sitemap-products.xml"), LastModified = DateTime.UtcNow },
    new() { Location = new Uri("https://example.com/sitemap-blog.xml"), LastModified = DateTime.UtcNow },
};

await using var stream = new MemoryStream();
await references.WriteToAsync(stream);
```

## Configuration

No configuration required.

## Dependencies

- Framework.Base

## Side Effects

None.
