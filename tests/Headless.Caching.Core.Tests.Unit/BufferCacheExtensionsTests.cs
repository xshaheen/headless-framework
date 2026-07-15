// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Caching;

namespace Tests;

/// <summary>
/// Tests for the <see cref="BufferCacheExtensions"/> fallback path: a plain <see cref="ICache"/> that does not
/// implement <see cref="IBufferCache"/> (an NSubstitute mock never does) must route raw reads/writes through the
/// generic <c>byte[]</c> members. The fast path against a real <see cref="IBufferCache"/> provider is covered by
/// the cross-provider conformance suite.
/// </summary>
public sealed class BufferCacheExtensionsTests
{
    private const string _Key = "buffer:fallback";

    private readonly ICache _cache = Substitute.For<ICache>();

    [Fact]
    public async Task try_get_to_or_fallback_writes_stored_bytes_and_returns_true_on_hit()
    {
        // given — the mock cache does not implement IBufferCache, so the helper takes the byte[] fallback branch
        var bytes = new byte[] { 10, 20, 30, 40 };
        _cache
            .GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<byte[]>(bytes, hasValue: true));
        var writer = new ArrayBufferWriter<byte>();

        // when
        var found = await _cache.TryGetToOrFallbackAsync(_Key, writer, CancellationToken.None);

        // then
        found.Should().BeTrue();
        writer.WrittenSpan.ToArray().Should().Equal(bytes);
        await _cache.Received(1).GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task try_get_to_or_fallback_returns_false_and_writes_nothing_on_miss()
    {
        // given
        _cache.GetAsync<byte[]>(_Key, Arg.Any<CancellationToken>()).Returns(CacheValue<byte[]>.NoValue);
        var writer = new ArrayBufferWriter<byte>();

        // when
        var found = await _cache.TryGetToOrFallbackAsync(_Key, writer, CancellationToken.None);

        // then
        found.Should().BeFalse();
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public async Task upsert_raw_or_fallback_materializes_sequence_and_upserts_via_generic_path()
    {
        // given — a pooled, multi-segment sequence (the buffer contract hands these in)
        var expected = new byte[] { 1, 2, 3, 4, 5, 6 };
        var sequence = _MultiSegment([1, 2], [3, 4], [5, 6]);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5), Tags = ["products"] };

        // when
        await _cache.UpsertRawOrFallbackAsync(_Key, sequence, options, CancellationToken.None);

        // then — the concatenated bytes and the passed options reach the typed upsert verbatim
        var calls = _UpsertCalls();
        calls.Should().ContainSingle();
        calls[0].Key.Should().Be(_Key);
        calls[0].Value.Should().Equal(expected);
        calls[0].Options.Duration.Should().Be(options.Duration);
        calls[0].Options.Tags.Should().Equal(options.Tags);
    }

    /// <summary>
    /// Reads the (key, value, options) of every <c>UpsertEntryAsync</c> call. Inspecting recorded calls sidesteps
    /// NSubstitute's argument-matcher engine, which mis-binds specs on this generic method; the unconfigured
    /// <see cref="ValueTask{Boolean}"/> return is a completed <see langword="false"/> the helper ignores.
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
        BufferSegment? first = null;
        BufferSegment? last = null;

        foreach (var segment in segments)
        {
            last = last is null ? first = new BufferSegment(segment) : last.Append(segment);
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new BufferSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }
}
