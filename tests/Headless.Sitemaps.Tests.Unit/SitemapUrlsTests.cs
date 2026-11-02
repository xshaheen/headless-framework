// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

public sealed class SitemapUrlsTests : SitemapTestBase
{
    // Serializable case labels (xUnit1045) instead of a TheoryData carrying non-serializable
    // List<SitemapUrl>; each label maps to its urls + expected XML via _GetCase.
    public static readonly TheoryData<string> TestData =
    [
        "basic",
        "with-priority-last-modified-change-frequency",
        "rfc-3986",
        "xml-entity-escape",
    ];

    private static (List<SitemapUrl> Urls, string Expected) _GetCase(string name)
    {
        return name switch
        {
            "basic" => (
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
            ),
            "with-priority-last-modified-change-frequency" => (
                [
                    new(
                        new Uri("https://www.example.com"),
                        new()
                        {
                            LastModified = new DateTime(year: 2021, month: 3, day: 15),
                            ChangeFrequency = ChangeFrequency.Daily,
                            Priority = 0.8f,
                        }
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
            ),
            "rfc-3986" => (
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
            ),
            "xml-entity-escape" => (
                [new(location: new Uri("https://www.example.com/ümlat.html&q=name"))],
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                    + "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
                    + "  <url>"
                    + "    <loc>https://www.example.com/%C3%BCmlat.html&amp;q=name</loc>"
                    + "  </url>"
                    + "</urlset>"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(memberName: nameof(TestData))]
    public async Task write_to_stream_test(string caseName)
    {
        var (urls, expected) = _GetCase(caseName);

        string result;

        await using (var stream = new MemoryStream())
        {
            await urls.WriteToAsync(stream, AbortToken);
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        AssertEquivalentXml(result, expected);
    }

    [Fact]
    public async Task should_add_xhtml_namespace_when_write_define_alternatives()
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
    public async Task should_write_alternative_urls_when_write_provide_any()
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
                new Uri("https://www.example.com/page"),
                new() { Images = [new SitemapImage(new Uri("https://www.example.com/image.jpg"))] }
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
                new Uri("https://www.example.com/page"),
                new()
                {
                    Images =
                    [
                        new SitemapImage(new Uri("https://www.example.com/image1.jpg")),
                        new SitemapImage(new Uri("https://www.example.com/image2.png")),
                    ],
                }
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
                options: new() { Images = [new SitemapImage(new Uri("https://www.example.com/shared-image.jpg"))] }
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
    public async Task should_filter_alternates_by_write_alternate_language_codes()
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
                options: new() { WriteAlternateLanguageCodes = ["en", "de"] } // Only write en and de
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

    #region Sharded WriteAsync (P1)

    [Fact]
    public async Task should_write_one_shard_when_urls_fit_in_one_sitemap()
    {
        var urls = _CreateUrls(100);

        var (count, shards) = await _CollectShardsAsync(urls);

        count.Should().Be(1);
        shards.Should().ContainSingle();
        shards[0].Index.Should().Be(0);
        shards[0].Count.Should().Be(100);
        Encoding.UTF8.GetString(shards[0].Bytes).Should().Contain("<loc>https://example.com/page0</loc>");
    }

    [Fact]
    public async Task should_write_one_shard_when_url_count_equals_max()
    {
        var urls = _CreateUrls(SitemapConstants.MaxSitemapUrls);

        var (count, shards) = await _CollectShardsAsync(urls);

        count.Should().Be(1);
        shards.Should().ContainSingle();
        shards[0].Count.Should().Be(SitemapConstants.MaxSitemapUrls);
    }

    [Fact]
    public async Task should_split_into_two_shards_when_url_count_exceeds_max_by_one()
    {
        var urls = _CreateUrls(SitemapConstants.MaxSitemapUrls + 1);

        var (count, shards) = await _CollectShardsAsync(urls);

        count.Should().Be(2);
        shards.Select(s => s.Index).Should().Equal(0, 1);
        shards.Select(s => s.Count).Should().Equal(SitemapConstants.MaxSitemapUrls, 1);

        var last = Encoding.UTF8.GetString(shards[1].Bytes);
        last.Should().Contain($"<loc>https://example.com/page{SitemapConstants.MaxSitemapUrls}</loc>");
        last.Should().NotContain("<loc>https://example.com/page0</loc>");
    }

    [Fact]
    public async Task should_write_shard_bytes_identical_to_write_to_async_on_the_same_slice()
    {
        var urls = _CreateMixedUrls();

        var (_, shards) = await _CollectShardsAsync(urls);

        shards.Should().HaveCount(2);
        shards[0].Bytes.Should().Equal(await _WriteToBytesAsync([.. urls.Take(SitemapConstants.MaxSitemapUrls)]));
        shards[1].Bytes.Should().Equal(await _WriteToBytesAsync([.. urls.Skip(SitemapConstants.MaxSitemapUrls)]));
    }

    [Fact]
    public async Task should_declare_xhtml_and_image_namespaces_only_in_shards_that_need_them()
    {
        // The first shard holds plain URLs only; the second holds one URL with images and alternates.
        var urls = _CreateUrls(SitemapConstants.MaxSitemapUrls);
        urls.Add(_CreateRichUrl(SitemapConstants.MaxSitemapUrls));

        var (_, shards) = await _CollectShardsAsync(urls);

        var first = _ReadHeader(shards[0].Bytes);
        first.Should().NotContain("xmlns:xhtml");
        first.Should().NotContain("xmlns:image");

        var second = _ReadHeader(shards[1].Bytes);
        second.Should().Contain("xmlns:xhtml=\"http://www.w3.org/1999/xhtml\"");
        second.Should().Contain("xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\"");
    }

    [Fact]
    public async Task should_write_the_same_shards_from_an_async_enumerable_as_from_an_enumerable()
    {
        var urls = _CreateMixedUrls();
        var (expectedCount, expected) = await _CollectShardsAsync(urls);

        var shards = new List<(int Index, int Count, byte[] Bytes)>();
        var count = await _ToAsyncEnumerable(urls)
            .WriteAsync(
                async (shard, ct) => shards.Add((shard.Index, shard.Count, await _WriteShardAsync(shard, ct))),
                AbortToken
            );

        count.Should().Be(expectedCount);
        shards.Should().HaveCount(expected.Count);

        for (var i = 0; i < shards.Count; i++)
        {
            shards[i].Index.Should().Be(expected[i].Index);
            shards[i].Count.Should().Be(expected[i].Count);
            shards[i].Bytes.Should().Equal(expected[i].Bytes);
        }
    }

    [Fact]
    public async Task should_not_call_on_shard_when_input_is_empty()
    {
        var calls = 0;

        var count = await new List<SitemapUrl>().WriteAsync(
            (_, _) =>
            {
                calls++;
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        var asyncCount = await _ToAsyncEnumerable([])
            .WriteAsync(
                (_, _) =>
                {
                    calls++;
                    return ValueTask.CompletedTask;
                },
                AbortToken
            );

        count.Should().Be(0);
        asyncCount.Should().Be(0);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_shard_is_written_after_its_callback_returns()
    {
        SitemapShard? captured = null;

        await _CreateUrls(10)
            .WriteAsync(
                (shard, _) =>
                {
                    captured = shard;
                    return ValueTask.CompletedTask;
                },
                AbortToken
            );

        await using var stream = new MemoryStream();
        var act = async () => await captured!.WriteToAsync(stream, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*shard 0 is no longer valid*");
        stream.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_on_shard_exception_and_stop_enumeration()
    {
        var enumerated = 0;
        var calls = 0;

        var urls = _CreateUrls((SitemapConstants.MaxSitemapUrls * 2) + 1)
            .Select(url =>
            {
                enumerated++;
                return url;
            });

        var act = async () =>
            await urls.WriteAsync(
                (_, _) =>
                {
                    calls++;
                    throw new InvalidDataException("upload failed");
                },
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("upload failed");
        calls.Should().Be(1);
        enumerated.Should().Be(SitemapConstants.MaxSitemapUrls);
    }

    [Fact]
    public async Task should_stop_between_urls_when_cancelled_by_a_shard_callback()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        var act = async () =>
            await _CreateUrls(SitemapConstants.MaxSitemapUrls + 1)
                .WriteAsync(
                    async (_, _) =>
                    {
                        calls++;
                        await cts.CancelAsync();
                    },
                    cts.Token
                );

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_not_call_on_shard_when_cancelled_before_start()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var calls = 0;

        var act = async () =>
            await _ToAsyncEnumerable(_CreateUrls(10))
                .WriteAsync(
                    (_, _) =>
                    {
                        calls++;
                        return ValueTask.CompletedTask;
                    },
                    cts.Token
                );

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_on_shard_is_null()
    {
        var act = async () => await _CreateUrls(1).WriteAsync(onShard: null!, AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static List<SitemapUrl> _CreateUrls(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => new SitemapUrl(new Uri($"https://example.com/page{i}")))];
    }

    private static SitemapUrl _CreateRichUrl(int i)
    {
        return new SitemapUrl(
            alternateLocations:
            [
                new() { Location = new Uri($"https://example.com/en/page{i}"), LanguageCode = "en" },
                new() { Location = new Uri($"https://example.com/ar/page{i}"), LanguageCode = "ar" },
            ],
            options: new SitemapUrlOptions
            {
                Images = [new SitemapImage(new Uri($"https://example.com/image{i}.jpg"))],
                Priority = 0.5f,
                ChangeFrequency = ChangeFrequency.Weekly,
                LastModified = new DateTime(2024, 1, 2),
            }
        );
    }

    // Two shards whose namespace needs differ: rich URLs are scattered through the first shard and the
    // second shard is plain, so a per-shard declaration that leaked across shards would change the bytes.
    private static List<SitemapUrl> _CreateMixedUrls()
    {
        return
        [
            .. Enumerable
                .Range(0, SitemapConstants.MaxSitemapUrls + 3)
                .Select(i =>
                    i < SitemapConstants.MaxSitemapUrls && i % 10_000 == 0
                        ? _CreateRichUrl(i)
                        : new SitemapUrl(new Uri($"https://example.com/page{i}"))
                ),
        ];
    }

    private static async Task<(int Count, List<(int Index, int Count, byte[] Bytes)> Shards)> _CollectShardsAsync(
        IEnumerable<SitemapUrl> urls
    )
    {
        var shards = new List<(int Index, int Count, byte[] Bytes)>();

        var count = await urls.WriteAsync(
            async (shard, ct) => shards.Add((shard.Index, shard.Count, await _WriteShardAsync(shard, ct))),
            AbortToken
        );

        return (count, shards);
    }

    private static async Task<byte[]> _WriteShardAsync(SitemapShard shard, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await shard.WriteToAsync(stream, cancellationToken);

        return stream.ToArray();
    }

    private static async Task<byte[]> _WriteToBytesAsync(List<SitemapUrl> urls)
    {
        await using var stream = new MemoryStream();
        await urls.WriteToAsync(stream, AbortToken);

        return stream.ToArray();
    }

    private static string _ReadHeader(byte[] bytes)
    {
        var xml = Encoding.UTF8.GetString(bytes);

        return xml[..xml.IndexOf("<url>", StringComparison.Ordinal)];
    }

    private static async IAsyncEnumerable<SitemapUrl> _ToAsyncEnumerable(IEnumerable<SitemapUrl> urls)
    {
        foreach (var url in urls)
        {
            await Task.Yield();
            yield return url;
        }
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
        var urls = new List<SitemapUrl> { new(new Uri("https://www.example.com"), new() { Priority = 0.8f }) };

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
            new(new Uri("https://www.example.com"), new() { ChangeFrequency = ChangeFrequency.Daily }),
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
    public async Task should_format_lastmod_as_yyyy_mm_dd()
    {
        var urls = new List<SitemapUrl>
        {
            new(new Uri("https://www.example.com"), new() { LastModified = new DateTime(2021, 3, 15) }),
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
