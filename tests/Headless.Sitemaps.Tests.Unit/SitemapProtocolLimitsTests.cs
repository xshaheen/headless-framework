// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

// The sitemaps.org protocol caps a sitemap file and a sitemap index file at 50,000 entries and 50 MB
// (52,428,800 bytes) uncompressed. The byte cap is a literal here, so these tests state the protocol rather than
// echo the package's own constant.
public sealed class SitemapProtocolLimitsTests : SitemapTestBase
{
    private const long _ProtocolMaxBytes = 52_428_800;
    private const int _ProtocolMaxEntries = 50_000;

    // About 1,150 characters, each "&" written as the five bytes "&amp;", so a size estimated from the string
    // length undercounts. 49,000 entries of it pass 50 MB while staying under the URL count limit.
    private static readonly Uri _LongUri = new(
        "https://example.com/catalog/item?" + string.Join('&', Enumerable.Range(0, 230).Select(i => $"k{i % 10}=v"))
    );

    [Fact]
    public async Task should_keep_every_shard_within_the_byte_limit_when_long_urls_pass_it_before_the_url_limit()
    {
        const int urlCount = 49_000;

        // One shared Uri keeps the input small; the sharder sizes each entry as written either way.
        var urls = Enumerable.Range(0, urlCount).Select(_ => new SitemapUrl(_LongUri)).ToList();
        var shards = new List<(int Count, long Bytes)>();

        var shardCount = await urls.WriteAsync(
            async (shard, ct) =>
            {
                await using var stream = new ByteCountingStream();
                await shard.WriteToAsync(stream, ct);
                shards.Add((shard.Count, stream.Length));
            },
            AbortToken
        );

        var oneEntryBytes = await _EntryBytesAsync(new SitemapUrl(_LongUri));

        shardCount.Should().BeGreaterThan(1);
        shards.Should().HaveCount(shardCount);
        shards.Should().OnlyContain(shard => shard.Bytes <= _ProtocolMaxBytes);
        shards.Sum(shard => shard.Count).Should().Be(urlCount);

        // The first shard is full: one more entry would have passed the cap, so the cut is not premature.
        (shards[0].Bytes + oneEntryBytes)
            .Should()
            .BeGreaterThan(_ProtocolMaxBytes);
    }

    [Fact]
    public async Task should_write_the_same_byte_limited_shards_from_an_async_enumerable()
    {
        var urls = Enumerable.Range(0, 49_000).Select(_ => new SitemapUrl(_LongUri)).ToList();
        var counts = new List<int>();

        await _ToAsyncEnumerable(urls)
            .WriteAsync(
                (shard, _) =>
                {
                    counts.Add(shard.Count);
                    return ValueTask.CompletedTask;
                },
                AbortToken
            );

        var syncCounts = new List<int>();

        await urls.WriteAsync(
            (shard, _) =>
            {
                syncCounts.Add(shard.Count);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        counts.Should().HaveCountGreaterThan(1).And.Equal(syncCounts);
    }

    [Fact]
    public async Task should_reject_an_index_of_more_than_50000_references_without_writing()
    {
        var references = _CreateReferences(
            _ProtocolMaxEntries + 1,
            index => new Uri($"https://example.com/s{index}.xml")
        );
        await using var output = new MemoryStream();

        var act = async () => await references.WriteToAsync(output, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("sitemapReferences");
        output.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_write_an_index_of_exactly_50000_references()
    {
        var references = _CreateReferences(_ProtocolMaxEntries, index => new Uri($"https://example.com/s{index}.xml"));
        await using var output = new MemoryStream();

        await references.WriteToAsync(output, AbortToken);

        var xml = Encoding.UTF8.GetString(output.ToArray());
        xml.Should().Contain("<loc>https://example.com/s49999.xml</loc>");
    }

    [Fact]
    public async Task should_reject_an_index_whose_xml_passes_the_byte_limit_without_writing()
    {
        // 49,000 long references stay under the entry limit but write more than 50 MB.
        var references = _CreateReferences(49_000, _ => _LongUri);
        await using var output = new MemoryStream();

        var act = async () => await references.WriteToAsync(output, AbortToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("sitemapReferences");
        output.Length.Should().Be(0);
    }

    private static List<SitemapReference> _CreateReferences(int count, Func<int, Uri> location)
    {
        return [.. Enumerable.Range(0, count).Select(index => new SitemapReference { Location = location(index) })];
    }

    // One entry's bytes in a file: a two-entry file minus a one-entry file.
    private static async Task<long> _EntryBytesAsync(SitemapUrl url)
    {
        return await _FileBytesAsync([url, url]) - await _FileBytesAsync([url]);
    }

    private static async Task<long> _FileBytesAsync(List<SitemapUrl> urls)
    {
        await using var stream = new ByteCountingStream();
        await urls.WriteToAsync(stream, AbortToken);

        return stream.Length;
    }

    private static async IAsyncEnumerable<SitemapUrl> _ToAsyncEnumerable(IEnumerable<SitemapUrl> urls)
    {
        foreach (var url in urls)
        {
            await Task.Yield();
            yield return url;
        }
    }

    // Counts the bytes a shard writes without holding 50 MB per shard in memory.
    private sealed class ByteCountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _length += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _length += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _length += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _length += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
