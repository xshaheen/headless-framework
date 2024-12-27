// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sitemaps;

namespace Tests;

public sealed class SitemapIndexBuilderTests : TestBase
{
    public static readonly TheoryData<List<SitemapReference>, string> TestData = new()
    {
        // basic
        {
            [
                new() { Location = new Uri("https://www.example.com/sitemap-main.xml") },
                new() { Location = new Uri("https://www.example.com/sitemap-jobs.xml") },
            ],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <sitemap>"
                + "    <loc>https://www.example.com/sitemap-main.xml</loc>"
                + "  </sitemap>"
                + "  <sitemap>"
                + "    <loc>https://www.example.com/sitemap-jobs.xml</loc>"
                + "  </sitemap>"
                + "</sitemapindex>"
        },
        // with last modified
        {
            [
                new()
                {
                    Location = new Uri("https://www.example.com/sitemap-main.xml"),
                    LastModified = new DateTime(2021, 3, 15),
                },
            ],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <sitemap>"
                + "    <loc>https://www.example.com/sitemap-main.xml</loc>"
                + "    <lastmod>2021-03-15</lastmod>"
                + "  </sitemap>"
                + "</sitemapindex>"
        },
        // Urls follow RFC-3986
        {
            [
                new() { Location = new Uri("https://www.Example.com/ümlaT-sitemap.xml") },
                new() { Location = new Uri("https://www.example.com/اداره-اعلانات") },
            ],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <sitemap>"
                + "    <loc>https://www.example.com/%C3%BCmlaT-sitemap.xml</loc>"
                + "  </sitemap>"
                + "  <sitemap>"
                + "    <loc>https://www.example.com/%D8%A7%D8%AF%D8%A7%D8%B1%D9%87-%D8%A7%D8%B9%D9%84%D8%A7%D9%86%D8%A7%D8%AA</loc>"
                + "  </sitemap>"
                + "</sitemapindex>"
        },
        // XML entity escape URLs
        {
            [new() { Location = new Uri("https://www.example.com/ümlat.html&q=name") }],
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                + "  <sitemap>"
                + "    <loc>https://www.example.com/%C3%BCmlat.html&amp;q=name</loc>"
                + "  </sitemap>"
                + "</sitemapindex>"
        },
    };

    [Theory]
    [MemberData(nameof(TestData))]
    public async Task write_sitemap_references_to_stream_test(List<SitemapReference> references, string expected)
    {
        string result;

        await using (var stream = new MemoryStream())
        {
            await references.WriteToAsync(stream);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }
}
