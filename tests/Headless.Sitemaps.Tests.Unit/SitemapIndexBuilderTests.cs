// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

public sealed class SitemapIndexBuilderTests : SitemapTestBase
{
    // Serializable case labels (xUnit1045) instead of a TheoryData carrying non-serializable
    // List<SitemapReference>; each label maps to its references + expected XML via _GetCase.
    public static readonly TheoryData<string> TestData =
    [
        "basic",
        "with-last-modified",
        "rfc-3986",
        "xml-entity-escape",
    ];

    private static (List<SitemapReference> References, string Expected) _GetCase(string name)
    {
        return name switch
        {
            "basic" => (
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
            ),
            "with-last-modified" => (
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
            ),
            "rfc-3986" => (
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
            ),
            "xml-entity-escape" => (
                [new() { Location = new Uri("https://www.example.com/ümlat.html&q=name") }],
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                    + "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                    + "  <sitemap>"
                    + "    <loc>https://www.example.com/%C3%BCmlat.html&amp;q=name</loc>"
                    + "  </sitemap>"
                    + "</sitemapindex>"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public async Task write_sitemap_references_to_stream_test(string caseName)
    {
        var (references, expected) = _GetCase(caseName);

        string result;

        await using (var stream = new MemoryStream())
        {
            await references.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }

    [Fact]
    public async Task should_respect_cancellation_token()
    {
        var references = Enumerable
            .Range(0, 100)
            .Select(i => new SitemapReference { Location = new Uri($"https://example.com/sitemap{i}.xml") })
            .ToList();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using var stream = new MemoryStream();

        var act = async () => await references.WriteToAsync(stream, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_handle_empty_references_collection()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var references = new List<SitemapReference>();

        // Empty sitemapindex produces self-closing tag
        const string expected = "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" />";

        string result;

        await using (var stream = new MemoryStream())
        {
            await references.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }
}
