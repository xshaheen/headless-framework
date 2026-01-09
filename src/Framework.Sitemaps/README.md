# Framework.Sitemaps

XML sitemap generation utilities for SEO.

## Problem Solved

Provides builders and models for generating XML sitemaps and sitemap indexes compliant with the sitemap protocol, supporting localized URLs, images, change frequency, and priority metadata.

## Key Features

- `SitemapUrl` - URL entry with metadata (lastmod, changefreq, priority)
- `SitemapUrls` - Collection builder for sitemap URLs
- `SitemapIndexBuilder` - Sitemap index generation for large sites
- `SitemapAlternateUrl` - Localized/alternate URL support (hreflang)
- `SitemapImage` - Image sitemap support
- `ChangeFrequency` - Standard frequency values (always, hourly, daily, weekly, etc.)

## Installation

```bash
dotnet add package Framework.Sitemaps
```

## Usage

### Basic Sitemap

```csharp
var urls = new SitemapUrls();

urls.Add(new SitemapUrl(
    location: new Uri("https://example.com/"),
    lastModified: DateTime.UtcNow,
    changeFrequency: ChangeFrequency.Daily,
    priority: 1.0f
));

urls.Add(new SitemapUrl(
    location: new Uri("https://example.com/about"),
    changeFrequency: ChangeFrequency.Monthly,
    priority: 0.8f
));

var xml = urls.Build();
```

### Localized URLs

```csharp
var alternates = new[]
{
    new SitemapAlternateUrl("en", new Uri("https://example.com/en/page")),
    new SitemapAlternateUrl("ar", new Uri("https://example.com/ar/page")),
};

urls.Add(new SitemapUrl(
    alternateLocations: alternates,
    lastModified: DateTime.UtcNow
));
```

### Sitemap Index

```csharp
var index = new SitemapIndexBuilder();

index.Add(new SitemapReference(
    new Uri("https://example.com/sitemap-products.xml"),
    DateTime.UtcNow
));

index.Add(new SitemapReference(
    new Uri("https://example.com/sitemap-blog.xml"),
    DateTime.UtcNow
));

var indexXml = index.Build();
```

## Configuration

No configuration required.

## Dependencies

None.

## Side Effects

None.
