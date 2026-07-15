// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Unit tests for <c>HeadlessOutputCacheStore</c> translation logic against a mocked <see cref="ICache"/>. Byte
/// fidelity over a real backend, real TTL/expiry, and real tag eviction are covered by the integration suite.
/// </summary>
public sealed class HeadlessOutputCacheStoreTests : TestBase
{
    private const string _Key = "ock:abc";
    private static readonly TimeSpan _ValidFor = TimeSpan.FromMinutes(5);

    private readonly ICache _cache = Substitute.For<ICache>();
    private readonly HeadlessOutputCacheStoreOptions _options = new() { DefaultExpiration = TimeSpan.FromMinutes(2) };

    private HeadlessOutputCacheStore _CreateStore()
    {
        return new(_cache, Options.Create(_options));
    }

    [Fact]
    public async Task get_async_returns_null_when_cache_reports_no_value()
    {
        // given
        _cache.GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>()).Returns(CacheValue<byte[]>.NoValue);
        var store = _CreateStore();

        // when
        var result = await store.GetAsync(_Key, AbortToken);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task get_async_returns_stored_bytes_when_present()
    {
        // given
        var bytes = new byte[] { 1, 2, 3, 4 };
        _cache
            .GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<byte[]>(bytes, hasValue: true));
        var store = _CreateStore();

        // when
        var result = await store.GetAsync(_Key, AbortToken);

        // then
        result.Should().BeSameAs(bytes);
    }

    [Fact]
    public async Task set_async_upserts_with_validfor_duration_and_supplied_tags()
    {
        // given
        var value = new byte[] { 9, 8, 7 };
        var tags = new[] { "products", "catalog" };
        var store = _CreateStore();

        // when
        await store.SetAsync(_Key, value, tags, _ValidFor, AbortToken);

        // then
        var calls = _UpsertCalls();
        calls.Should().ContainSingle();
        calls[0].Key.Should().Be(_Key);
        calls[0].Value.Should().BeSameAs(value);
        calls[0].Options.Duration.Should().Be(_ValidFor);
        calls[0].Options.Tags.Should().Equal(tags);
    }

    [Fact]
    public async Task set_async_passes_null_tags_when_tags_are_null_or_empty()
    {
        // given
        var store = _CreateStore();

        // when
        await store.SetAsync(_Key, [0x1], tags: null, _ValidFor, AbortToken);
        await store.SetAsync(_Key, [0x1], tags: [], _ValidFor, AbortToken);

        // then — the engine indexes nothing for tagless entries
        var calls = _UpsertCalls();
        calls.Should().HaveCount(2);
        calls.Should().OnlyContain(c => c.Options.Tags == null);
    }

    [Fact]
    public async Task set_async_falls_back_to_default_expiration_for_non_positive_validfor()
    {
        // given
        var store = _CreateStore();

        // when
        await store.SetAsync(_Key, [0x1], tags: null, TimeSpan.Zero, AbortToken);
        await store.SetAsync(_Key, [0x1], tags: null, TimeSpan.FromSeconds(-1), AbortToken);

        // then
        _UpsertCalls().Should().OnlyContain(c => c.Options.Duration == _options.DefaultExpiration);
    }

    [Fact]
    public async Task evict_by_tag_delegates_to_remove_by_tag()
    {
        // given
        var store = _CreateStore();

        // when
        await store.EvictByTagAsync("products", AbortToken);

        // then
        await _cache.Received(1).RemoveByTagAsync("products", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task buffer_set_async_materializes_a_multi_segment_sequence_into_the_same_bytes()
    {
        // given — a pooled, multi-segment ReadOnlySequence (the buffer-store contract hands these in)
        var expected = new byte[] { 1, 2, 3, 4, 5, 6 };
        var sequence = _MultiSegment([1, 2], [3, 4], [5, 6]);
        var tags = new[] { "products" };
        var store = _CreateStore();

        // when
        await store.SetAsync(_Key, sequence, tags, _ValidFor, AbortToken);

        // then — the concatenation of every segment is persisted verbatim
        var calls = _UpsertCalls();
        calls.Should().ContainSingle();
        calls[0].Value.Should().Equal(expected);
        calls[0].Options.Tags.Should().Equal(tags);
    }

    [Fact]
    public async Task buffer_try_get_async_writes_stored_bytes_and_returns_true()
    {
        // given
        var bytes = new byte[] { 10, 20, 30 };
        _cache
            .GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<byte[]>(bytes, hasValue: true));
        var pipe = new Pipe();
        var store = _CreateStore();

        // when
        var found = await store.TryGetAsync(_Key, pipe.Writer, AbortToken);
        await pipe.Writer.CompleteAsync();

        // then
        found.Should().BeTrue();
        (await _ReadAllAsync(pipe.Reader)).Should().Equal(bytes);
    }

    [Fact]
    public async Task buffer_try_get_async_returns_false_and_writes_nothing_on_miss()
    {
        // given
        _cache.GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>()).Returns(CacheValue<byte[]>.NoValue);
        var pipe = new Pipe();
        var store = _CreateStore();

        // when
        var found = await store.TryGetAsync(_Key, pipe.Writer, AbortToken);
        await pipe.Writer.CompleteAsync();

        // then
        found.Should().BeFalse();
        (await _ReadAllAsync(pipe.Reader)).Should().BeEmpty();
    }

    // The tests above mock ICache, which never satisfies `is IBufferCache`, so they only exercise the byte[]
    // fallback. These two drive the store over a REAL IBufferCache (InMemoryCache) so the zero-intermediate-copy
    // buffer fast path (UpsertRawAsync / TryGetToAsync) is actually traversed end to end.
    [Fact]
    public async Task buffer_round_trips_byte_identical_content_through_the_real_ibuffercache_fast_path()
    {
        // given — InMemoryCache implements IBufferCache, so the store takes the raw fast path, not the byte[] copy
        using var bufferCache = new InMemoryCache(new FakeTimeProvider(), new InMemoryCacheOptions());
        var store = new HeadlessOutputCacheStore(bufferCache, Options.Create(_options));
        var expected = new byte[] { 1, 2, 3, 4, 5, 6 };
        var sequence = _MultiSegment([1, 2], [3, 4], [5, 6]);
        var pipe = new Pipe();

        // when — write via the buffer SetAsync, then read back via the PipeWriter buffer TryGetAsync
        await store.SetAsync(_Key, sequence, new[] { "products" }, _ValidFor, AbortToken);
        var found = await store.TryGetAsync(_Key, pipe.Writer, AbortToken);
        await pipe.Writer.CompleteAsync();

        // then — the IBufferCache branch round-trips the payload verbatim
        found.Should().BeTrue();
        (await _ReadAllAsync(pipe.Reader)).Should().Equal(expected);
    }

    [Fact]
    public async Task buffer_try_get_async_returns_false_and_writes_nothing_on_a_real_ibuffercache_miss()
    {
        // given — a real IBufferCache with nothing stored under the key
        using var bufferCache = new InMemoryCache(new FakeTimeProvider(), new InMemoryCacheOptions());
        var store = new HeadlessOutputCacheStore(bufferCache, Options.Create(_options));
        var pipe = new Pipe();

        // when
        var found = await store.TryGetAsync(_Key, pipe.Writer, AbortToken);
        await pipe.Writer.CompleteAsync();

        // then — the buffer fast path reports a miss and leaves the pipe empty
        found.Should().BeFalse();
        (await _ReadAllAsync(pipe.Reader)).Should().BeEmpty();
    }

    [Fact]
    public async Task guards_reject_invalid_key_and_value_before_touching_the_cache()
    {
        // given
        var store = _CreateStore();

        // when / then
        await FluentActions
            .Awaiting(() => store.GetAsync("", AbortToken).AsTask())
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() => store.SetAsync("", [0x1], null, _ValidFor, AbortToken).AsTask())
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() => store.SetAsync(_Key, null!, null, _ValidFor, AbortToken).AsTask())
            .Should()
            .ThrowAsync<ArgumentException>();

        await _cache.DidNotReceiveWithAnyArgs().GetAsync<byte[]>(default!, AbortToken);
        await _cache.DidNotReceiveWithAnyArgs().UpsertEntryAsync<byte[]>(default!, default, default, AbortToken);
    }

    [Fact]
    public async Task guards_reject_invalid_input_on_buffer_and_evict_members()
    {
        // given
        var store = _CreateStore();
        var pipe = new Pipe();

        // when / then — the buffer-store and eviction members guard before touching the cache, same as the
        // byte[] members above (those overloads have their own argument-validation path the cache never sees)
        await FluentActions
            .Awaiting(() =>
                store
                    .SetAsync("", ReadOnlySequence<byte>.Empty, ReadOnlyMemory<string>.Empty, _ValidFor, AbortToken)
                    .AsTask()
            )
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() => store.TryGetAsync("", pipe.Writer, AbortToken).AsTask())
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() => store.TryGetAsync(_Key, null!, AbortToken).AsTask())
            .Should()
            .ThrowAsync<ArgumentException>();
        await FluentActions
            .Awaiting(() => store.EvictByTagAsync("", AbortToken).AsTask())
            .Should()
            .ThrowAsync<ArgumentException>();

        await _cache.DidNotReceiveWithAnyArgs().GetAsync<byte[]>(default!, AbortToken);
        await _cache.DidNotReceiveWithAnyArgs().UpsertEntryAsync<byte[]>(default!, default, default, AbortToken);
        await _cache.DidNotReceiveWithAnyArgs().RemoveByTagAsync(default!, AbortToken);
    }

    /// <summary>
    /// Reads the (key, value, options) of every <c>UpsertEntryAsync</c> call the store issued. Inspecting
    /// recorded calls sidesteps NSubstitute's argument-matcher engine, which mis-binds specs on this generic
    /// method; the unconfigured <see cref="ValueTask{Boolean}"/> return is a completed <see langword="false"/> the store
    /// ignores.
    /// </summary>
    private IReadOnlyList<(string Key, byte[] Value, CacheEntryOptions Options)> _UpsertCalls()
    {
        return _cache
            .ReceivedCalls()
            .Where(call =>
                string.Equals(call.GetMethodInfo().Name, nameof(ICache.UpsertEntryAsync), StringComparison.Ordinal)
            )
            .Select(call =>
            {
                var args = call.GetArguments();

                return ((string)args[0]!, (byte[])args[1]!, (CacheEntryOptions)args[2]!);
            })
            .ToList();
    }

    private static ReadOnlySequence<byte> _MultiSegment(params byte[][] segments)
    {
        Segment? first = null;
        Segment? last = null;

        foreach (var segment in segments)
        {
            last = last is null ? first = new Segment(segment) : last.Append(segment);
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private static async Task<byte[]> _ReadAllAsync(PipeReader reader)
    {
        var result = await reader.ReadAsync(AbortToken);
        var bytes = result.Buffer.ToArray();
        reader.AdvanceTo(result.Buffer.End);
        await reader.CompleteAsync();

        return bytes;
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }
}
