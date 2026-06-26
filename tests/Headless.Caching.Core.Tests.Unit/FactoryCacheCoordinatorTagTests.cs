// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class FactoryCacheCoordinatorTagTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeFactoryCacheStore _store = new();

    [Fact]
    public async Task should_reject_empty_tag_in_options()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1), Tags = ["valid", ""] };

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "key", _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _store.SetEntryCalls.Should().Be(0, "validation must reject the options before anything is written");
    }

    [Fact]
    public async Task should_reject_tag_longer_than_envelope_limit()
    {
        // given
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(1),
            Tags = [new string('x', ushort.MaxValue + 1)],
        };

        // when
        var act = async () =>
            await _CreateCoordinator()
                .GetOrAddAsync(_store, "key", _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_persist_options_tags_on_simple_write()
    {
        // given
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1), Tags = ["t1", "t2"] };

        // when
        await _CreateCoordinator()
            .GetOrAddAsync(_store, "key", _ => ValueTask.FromResult<string?>("value"), options, AbortToken);

        // then
        var entry = _store.GetEntry("key");
        entry.Should().NotBeNull();
        entry!.Tags.Should().BeEquivalentTo("t1", "t2");
    }

    [Fact]
    public async Task should_prefer_options_tags_over_existing_entry_tags()
    {
        // given — a logically-expired but physically-present entry carrying old tags
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(
            "key",
            "old",
            logicalExpiresAt: now.AddMilliseconds(-10),
            physicalExpiresAt: now.AddMinutes(5),
            tags: ["old-tag", "kept-tag"]
        );

        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1), Tags = ["kept-tag", "new-tag"] };
        IReadOnlyCollection<string>? observedContextTags = null;

        // when
        await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                "key",
                (context, _) =>
                {
                    observedContextTags = context.Tags;
                    return ValueTask.FromResult(context.Modified("new"));
                },
                options,
                AbortToken
            );

        // then — call-provided tags win over the existing entry's tags
        observedContextTags.Should().BeEquivalentTo("kept-tag", "new-tag");
        var entry = _store.GetEntry("key");
        entry!.Tags.Should().BeEquivalentTo("kept-tag", "new-tag");
    }

    [Fact]
    public async Task should_carry_existing_tags_forward_when_options_tags_are_null()
    {
        // given — a stale entry with tags, refreshed via NotModified without call-provided tags
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _store.SetEntry(
            "key",
            "value",
            logicalExpiresAt: now.AddMilliseconds(-10),
            physicalExpiresAt: now.AddMinutes(5),
            tags: ["keep-me"]
        );

        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };

        // when
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                "key",
                (context, _) => ValueTask.FromResult(context.NotModified()),
                options,
                AbortToken
            );

        // then — the restamped entry keeps its tags
        result.Value.Should().Be("value");
        var entry = _store.GetEntry("key");
        entry!.Tags.Should().BeEquivalentTo("keep-me");
    }

    [Fact]
    public async Task should_persist_context_tag_mutation_on_conditional_write()
    {
        // given — a cold cache; the factory assigns the tags through the context
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };

        // when
        await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                "key",
                (context, _) =>
                {
                    context.Tags = ["factory-tag"];
                    return ValueTask.FromResult(context.Modified("value"));
                },
                options,
                AbortToken
            );

        // then
        var entry = _store.GetEntry("key");
        entry!.Tags.Should().BeEquivalentTo("factory-tag");
    }

    [Fact]
    public async Task should_preserve_created_at_when_notmodified_restamps_entry()
    {
        // given — a logically-expired but physically-present entry carrying a known birth time
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var createdAt = now.AddHours(-1);
        _store.SetEntry(
            "key",
            "value",
            logicalExpiresAt: now.AddMilliseconds(-10),
            physicalExpiresAt: now.AddMinutes(5),
            createdAt: createdAt
        );

        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(1) };

        // when — the factory returns NotModified, re-stamping the existing value as fresh
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                "key",
                (context, _) => ValueTask.FromResult(context.NotModified()),
                options,
                AbortToken
            );

        // then — the re-stamp carries the original birth time forward instead of stamping it to now
        result.Value.Should().Be("value");
        var entry = _store.GetEntry("key");
        entry!.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public async Task should_preserve_created_at_when_failsafe_throttle_restamps_reserve()
    {
        // given — a fresh reserve with a known birth time and fail-safe enabled
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var createdAt = now;
        _store.SetEntry(
            "key",
            "stale",
            logicalExpiresAt: now.AddSeconds(1),
            physicalExpiresAt: now.AddMinutes(5),
            createdAt: createdAt
        );

        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromSeconds(5),
            IsFailSafeEnabled = true,
            FailSafeThrottleDuration = TimeSpan.FromSeconds(10),
        };

        // advance past logical expiry so the reserve is stale but still physically present
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // when — the factory throws, activating fail-safe and the throttle re-stamp
        var result = await _CreateCoordinator()
            .GetOrAddAsync<string>(
                _store,
                "key",
                _ => throw new InvalidOperationException("downstream unavailable"),
                options,
                AbortToken
            );

        // then — the throttle re-stamp preserves the original birth time, not the advanced clock
        result.Value.Should().Be("stale");
        result.IsStale.Should().BeTrue();
        var entry = _store.GetEntry("key");
        entry!.CreatedAt.Should().Be(createdAt);
    }

    private FactoryCacheCoordinator _CreateCoordinator() =>
        new(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance);
}
