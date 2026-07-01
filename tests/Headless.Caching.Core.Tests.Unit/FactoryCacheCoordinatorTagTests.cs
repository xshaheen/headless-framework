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
    private readonly List<FactoryCacheCoordinator> _coordinators = [];

    protected override ValueTask DisposeAsyncCore()
    {
        foreach (var coordinator in _coordinators)
        {
            coordinator.Dispose();
        }

        _coordinators.Clear();

        return base.DisposeAsyncCore();
    }

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

    // Tracks every coordinator so it is disposed in teardown; inline fluent calls cannot take a `using`.
    private FactoryCacheCoordinator _CreateCoordinator()
    {
        var coordinator = new FactoryCacheCoordinator(_timeProvider, NullLogger<FactoryCacheCoordinator>.Instance);
        _coordinators.Add(coordinator);

        return coordinator;
    }
}
