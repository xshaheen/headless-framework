// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

#pragma warning disable xUnit1045 // Avoid using TheoryData type arguments that might not be serializable
public sealed class SitemapUrlsTests : SitemapTestBase
{
    public static readonly TheoryData<List<SitemapUrl>, string> TestData = new()
    {
        // basic
        {
            [
                new(location: new Uri("https://www.example.com")),
                new(location: new Uri("https://www.example.com/contact-us")),
            ],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <url>"
                + "    <loc>https://www.example.com/</loc>"
                + "  </url>"
                + "  <url>"
                + "    <loc>https://www.example.com/contact-us</loc>"
                + "  </url>"
                + "</urlset>"
        },
        // with priority, last modified, change frequency
        {
            [
                new(
                    location: new Uri("https://www.example.com"),
                    lastModified: new DateTime(year: 2021, month: 3, day: 15),
                    changeFrequency: ChangeFrequency.Daily,
                    priority: 0.8f
                ),
            ],
            "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <url>"
                + "   <loc>https://www.example.com/</loc>"
                + "   <priority>0.8</priority>"
                + "   <changefreq>daily</changefreq>"
                + "   <lastmod>2021-03-15</lastmod>"
                + "  </url>"
                + "</urlset>"
        },
        // Urls follow RFC-3986
        {
            [
                new(location: new Uri("https://www.Example.com/ümlaT.html")),
                new(location: new Uri("https://www.example.com/اداره-اعلانات")),
            ],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <url>"
                + "    <loc>https://www.example.com/%C3%BCmlaT.html</loc>"
                + "  </url>"
                + "  <url>"
                + "    <loc>https://www.example.com/%D8%A7%D8%AF%D8%A7%D8%B1%D9%87-%D8%A7%D8%B9%D9%84%D8%A7%D9%86%D8%A7%D8%AA</loc>"
                + "  </url>"
                + "</urlset>"
        },
        // XML entity escape URLs
        {
            [new(location: new Uri("https://www.example.com/ümlat.html&q=name"))],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <url>"
                + "    <loc>https://www.example.com/%C3%BCmlat.html&amp;q=name</loc>"
                + "  </url>"
                + "</urlset>"
        },
    };

    [Theory]
    [MemberData(memberName: nameof(TestData))]
    public async Task write_to_stream_test(List<SitemapUrl> urls, string expected)
    {
        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }

    [Fact]
    public async Task write_should_add_xhtml_namespace_when_define_alternatives()
    {
        var urls = new List<SitemapUrl>
        {
            new(
                alternateLocations:
                [
                    new() { Location = new Uri("https://www.example.com/ar/page.html"), LanguageCode = "ar" },
                    new() { Location = new Uri("https://www.example.com/en/page.html"), LanguageCode = "en" },
                ]
            ),
        };

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        result.Should().Contain("xmlns:xhtml=\"http://www.w3.org/1999/xhtml\"");
    }

    [Fact]
    public async Task write_should_write_alternative_urls_when_provide_any()
    {
        var urls = new List<SitemapUrl>
        {
            new(
                alternateLocations:
                [
                    new() { Location = new Uri("https://www.example.com/english/page.html"), LanguageCode = "en" },
                    new() { Location = new Uri("https://www.example.com/deutsch/page.html"), LanguageCode = "de" },
                    new()
                    {
                        Location = new Uri("https://www.example.com/schweiz-deutsch/page.html"),
                        LanguageCode = "de-ch",
                    },
                ]
            ),
        };

        const string expected =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<urlset xmlns:xhtml=\"http://www.w3.org/1999/xhtml\" xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
            + "  <url>"
            + "    <loc>https://www.example.com/english/page.html</loc>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"https://www.example.com/english/page.html\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de\" href=\"https://www.example.com/deutsch/page.html\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de-ch\" href=\"https://www.example.com/schweiz-deutsch/page.html\"/>"
            + "  </url>"
            + "  <url>"
            + "    <loc>https://www.example.com/deutsch/page.html</loc>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"https://www.example.com/english/page.html\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de\" href=\"https://www.example.com/deutsch/page.html\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de-ch\" href=\"https://www.example.com/schweiz-deutsch/page.html\"/>"
            + "  </url>"
            + "  <url>"
            + "    <loc>https://www.example.com/schweiz-deutsch/page.html</loc>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"https://www.example.com/english/page.html\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de\" href=\"https://www.example.com/deutsch/page.html\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de-ch\" href=\"https://www.example.com/schweiz-deutsch/page.html\"/>"
            + "  </url>"
            + "</urlset>";

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }

    #region Image Support Tests (P1)

    [Fact]
    public async Task should_add_image_namespace_when_images_present()
    {
        var urls = new List<SitemapUrl>
        {
            new(
                location: new Uri("https://www.example.com/page"),
                images: [new SitemapImage(new Uri("https://www.example.com/image.jpg"))]
            ),
        };

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        result.Should().Contain("xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\"");
    }

    [Fact]
    public async Task should_write_image_elements_within_url()
    {
        var urls = new List<SitemapUrl>
        {
            new(
                location: new Uri("https://www.example.com/page"),
                images:
                [
                    new SitemapImage(new Uri("https://www.example.com/image1.jpg")),
                    new SitemapImage(new Uri("https://www.example.com/image2.png")),
                ]
            ),
        };

        const string expected =
            "<urlset xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\" xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
            + "  <url>"
            + "    <loc>https://www.example.com/page</loc>"
            + "    <image:image>"
            + "      <image:loc>https://www.example.com/image1.jpg</image:loc>"
            + "    </image:image>"
            + "    <image:image>"
            + "      <image:loc>https://www.example.com/image2.png</image:loc>"
            + "    </image:image>"
            + "  </url>"
            + "</urlset>";

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }

    [Fact]
    public async Task should_write_images_in_alternate_url_entries()
    {
        var urls = new List<SitemapUrl>
        {
            new(
                alternateLocations:
                [
                    new() { Location = new Uri("https://www.example.com/en/page"), LanguageCode = "en" },
                    new() { Location = new Uri("https://www.example.com/de/page"), LanguageCode = "de" },
                ],
                images: [new SitemapImage(new Uri("https://www.example.com/shared-image.jpg"))]
            ),
        };

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        // Both alternate URLs should contain the image
        result.Should().Contain("xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\"");

        // Count occurrences of image:loc - should be 2 (one for each alternate URL)
        var imageLocCount = result.Split("<image:loc>").Length - 1;
        imageLocCount.Should().Be(2);
    }

    #endregion

    #region WriteAlternateLanguageCodes Filter (P1)

    [Fact]
    public async Task should_filter_alternates_by_WriteAlternateLanguageCodes()
    {
        var urls = new List<SitemapUrl>
        {
            new(
                alternateLocations:
                [
                    new() { Location = new Uri("https://www.example.com/en/page"), LanguageCode = "en" },
                    new() { Location = new Uri("https://www.example.com/de/page"), LanguageCode = "de" },
                    new() { Location = new Uri("https://www.example.com/fr/page"), LanguageCode = "fr" },
                ],
                writeAlternateLanguageCodes: ["en", "de"] // Only write en and de
            ),
        };

        const string expected =
            "<urlset xmlns:xhtml=\"http://www.w3.org/1999/xhtml\" xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
            + "  <url>"
            + "    <loc>https://www.example.com/en/page</loc>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"https://www.example.com/en/page\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de\" href=\"https://www.example.com/de/page\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"fr\" href=\"https://www.example.com/fr/page\"/>"
            + "  </url>"
            + "  <url>"
            + "    <loc>https://www.example.com/de/page</loc>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"en\" href=\"https://www.example.com/en/page\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"de\" href=\"https://www.example.com/de/page\"/>"
            + "    <xhtml:link rel=\"alternate\" hreflang=\"fr\" href=\"https://www.example.com/fr/page\"/>"
            + "  </url>"
            + "</urlset>";

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);

        // Should NOT contain fr as main <loc> (only en and de should have url entries)
        var locCount = result.Split("<loc>").Length - 1;
        locCount.Should().Be(2); // Only en and de main URLs
    }

    #endregion

    #region URL Splitting Tests (P1)

    [Fact]
    public async Task should_split_into_multiple_streams_when_exceeding_max()
    {
        // Create 50,001 URLs (just over the max)
        var urls = Enumerable
            .Range(0, SitemapConstants.MaxSitemapUrls + 1)
            .Select(i => new SitemapUrl(new Uri($"https://example.com/page{i}")))
            .ToList();

        var streams = await urls.WriteAsync(AbortToken);

        streams.Should().HaveCount(2);
        streams[0].Length.Should().BeGreaterThan(0);
        streams[1].Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task should_return_single_stream_for_small_url_list()
    {
        var urls = Enumerable
            .Range(0, 100)
            .Select(i => new SitemapUrl(new Uri($"https://example.com/page{i}")))
            .ToList();

        var streams = await urls.WriteAsync(AbortToken);

        streams.Should().HaveCount(1);
    }

    #endregion

    #region Edge Cases (P2)

    [Fact]
    public async Task should_handle_empty_url_collection()
    {
        var urls = new List<SitemapUrl>();

        // Empty urlset produces self-closing tag
        const string expected = "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" />";

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }

    [Fact]
    public async Task should_respect_cancellation_token()
    {
        var urls = Enumerable
            .Range(0, 1000)
            .Select(i => new SitemapUrl(new Uri($"https://example.com/page{i}")))
            .ToList();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using var stream = new MemoryStream();

        var act = async () => await urls.WriteToAsync(stream, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_format_priority_with_one_decimal()
    {
        var urls = new List<SitemapUrl> { new(location: new Uri("https://www.example.com"), priority: 0.8f) };

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        result.Should().Contain("<priority>0.8</priority>");
        result.Should().NotContain("0.80");
    }

    [Fact]
    public async Task should_format_changefreq_lowercase()
    {
        var urls = new List<SitemapUrl>
        {
            new(location: new Uri("https://www.example.com"), changeFrequency: ChangeFrequency.Daily),
        };

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        result.Should().Contain("<changefreq>daily</changefreq>");
        result.Should().NotContain("Daily");
    }

    [Fact]
    public async Task should_format_lastmod_as_yyyy_MM_dd()
    {
        var urls = new List<SitemapUrl>
        {
            new(location: new Uri("https://www.example.com"), lastModified: new DateTime(2021, 3, 15)),
        };

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        result.Should().Contain("<lastmod>2021-03-15</lastmod>");
    }

    #endregion
}
