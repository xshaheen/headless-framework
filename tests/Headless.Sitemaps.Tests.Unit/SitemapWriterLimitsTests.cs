// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

// SitemapUrls.WriteToAsync writes one file, so it refuses input past the sitemaps.org limits (50,000 <url>
// elements, 52,428,800 bytes uncompressed) instead of writing a file crawlers reject. A shard from WriteAsync is
// already cut to the limits and always writes. The limits are literals here, so these tests state the protocol
// rather than echo the package's own constants.
public sealed class SitemapWriterLimitsTests : SitemapTestBase
{
    private const int _ProtocolMaxUrls = 50_000;

    // About 1,150 characters, each "&" written as the five bytes "&amp;": 49,000 of them pass 50 MB.
    private static readonly Uri _LongUri = new(
        "https://example.com/catalog/item?" + string.Join('&', Enumerable.Range(0, 230).Select(i => $"k{i % 10}=v"))
    );

    [Fact]
    public async Task should_refuse_more_than_50000_urls_without_writing()
    {
        var urls = _CreateUrls(_ProtocolMaxUrls + 1);
        await using var output = new MemoryStream();

        var act = async () => await urls.WriteToAsync(output, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("sitemapUrls");
        output.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_alternates_that_write_more_than_50000_url_elements()
    {
        // 25,001 two-language entries write 50,002 <url> elements.
        var urls = Enumerable
            .Range(0, (_ProtocolMaxUrls / 2) + 1)
            .Select(i => new SitemapUrl(
                alternateLocations:
                [
                    new() { Location = new Uri($"https://example.com/en/page{i}"), LanguageCode = "en" },
                    new() { Location = new Uri($"https://example.com/ar/page{i}"), LanguageCode = "ar" },
                ]
            ))
            .ToList();
        await using var output = new MemoryStream();

        var act = async () => await urls.WriteToAsync(output, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("sitemapUrls");
        output.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_urls_whose_xml_passes_50_mb_without_writing()
    {
        // One shared Uri keeps the input small; the writer sizes each entry as written either way.
        var urls = Enumerable.Range(0, 49_000).Select(_ => new SitemapUrl(_LongUri)).ToList();
        await using var output = new MemoryStream();

        var act = async () => await urls.WriteToAsync(output, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("sitemapUrls");
        output.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_write_exactly_50000_urls()
    {
        var urls = _CreateUrls(_ProtocolMaxUrls);
        await using var output = new MemoryStream();

        await urls.WriteToAsync(output, AbortToken);

        Encoding.UTF8.GetString(output.ToArray()).Should().Contain("<loc>https://example.com/page49999</loc>");
    }

    [Fact]
    public async Task should_write_a_shard_whose_single_entry_alone_passes_50_mb()
    {
        // One entry with 700,000 images writes about 60 MB. Its images cannot be split across files, so WriteAsync
        // gives it a shard of its own, and writing that shard must not throw. The direct writer refuses it.
        var image = new SitemapImage(new Uri("https://example.com/i.jpg"));
        var url = new SitemapUrl(
            new Uri("https://example.com/gallery"),
            new SitemapUrlOptions { Images = Enumerable.Repeat(image, 700_000) }
        );
        var counts = new List<int>();

        var shardCount = await new[] { url }.WriteAsync(
            async (shard, ct) =>
            {
                counts.Add(shard.Count);
                await shard.WriteToAsync(Stream.Null, ct);
            },
            AbortToken
        );

        shardCount.Should().Be(1);
        counts.Should().Equal(1);

        var direct = async () => await new[] { url }.WriteToAsync(Stream.Null, AbortToken);

        (await direct.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("sitemapUrls");
    }

    private static List<SitemapUrl> _CreateUrls(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => new SitemapUrl(new Uri($"https://example.com/page{i}")))];
    }
}
