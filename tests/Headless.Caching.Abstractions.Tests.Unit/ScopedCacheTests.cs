// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ScopedCacheTests : TestBase
{
    private static readonly TimeSpan _DefaultExpiration = TimeSpan.FromMinutes(5);
    private readonly FakeTimeProvider _timeProvider = new();
    private string _currentScope = "scope-a";

    private ScopedCache<string> _CreateSut()
    {
        var cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        return new ScopedCache<string>(cache, () => _currentScope);
    }

    [Fact]
    public async Task should_isolate_cache_entries_by_scope()
    {
        // given
        var sut = _CreateSut();

        _currentScope = "scope-a";
        await sut.UpsertAsync("key", "value-a", _DefaultExpiration, AbortToken);

        _currentScope = "scope-b";
        await sut.UpsertAsync("key", "value-b", _DefaultExpiration, AbortToken);

        // when / then
        _currentScope = "scope-a";
        var resultA = await sut.GetAsync("key", AbortToken);
        resultA.HasValue.Should().BeTrue();
        resultA.Value.Should().Be("value-a");

        _currentScope = "scope-b";
        var resultB = await sut.GetAsync("key", AbortToken);
        resultB.HasValue.Should().BeTrue();
        resultB.Value.Should().Be("value-b");
    }

    [Fact]
    public async Task should_not_find_key_from_different_scope()
    {
        // given
        var sut = _CreateSut();

        _currentScope = "scope-a";
        await sut.UpsertAsync("key", "value", _DefaultExpiration, AbortToken);

        // when
        _currentScope = "scope-b";
        var result = await sut.GetAsync("key", AbortToken);

        // then
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_only_for_current_scope()
    {
        // given
        var sut = _CreateSut();

        _currentScope = "scope-a";
        await sut.UpsertAsync("key", "value-a", _DefaultExpiration, AbortToken);

        _currentScope = "scope-b";
        await sut.UpsertAsync("key", "value-b", _DefaultExpiration, AbortToken);

        // when — remove from scope-a
        _currentScope = "scope-a";
        await sut.RemoveAsync("key", AbortToken);

        // then — scope-b unaffected
        _currentScope = "scope-a";
        var resultA = await sut.GetAsync("key", AbortToken);
        resultA.HasValue.Should().BeFalse();

        _currentScope = "scope-b";
        var resultB = await sut.GetAsync("key", AbortToken);
        resultB.HasValue.Should().BeTrue();
        resultB.Value.Should().Be("value-b");
    }

    [Fact]
    public async Task should_scope_get_all_and_unscope_returned_keys()
    {
        // given
        var sut = _CreateSut();

        _currentScope = "scope-a";
        await sut.UpsertAsync("key1", "a1", _DefaultExpiration, AbortToken);
        await sut.UpsertAsync("key2", "a2", _DefaultExpiration, AbortToken);

        // when
        var result = await sut.GetAllAsync(["key1", "key2"], AbortToken);

        // then — returned keys are unscoped
        result.Should().ContainKey("key1");
        result.Should().ContainKey("key2");
        result["key1"].Value.Should().Be("a1");
        result["key2"].Value.Should().Be("a2");
    }

    [Fact]
    public async Task should_scope_upsert_all()
    {
        // given
        var sut = _CreateSut();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key1"] = "val1",
            ["key2"] = "val2",
        };

        _currentScope = "scope-a";
        await sut.UpsertAllAsync(values, _DefaultExpiration, AbortToken);

        // when — different scope sees nothing
        _currentScope = "scope-b";
        var result = await sut.GetAllAsync(["key1", "key2"], AbortToken);

        // then
        result["key1"].HasValue.Should().BeFalse();
        result["key2"].HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_scope_remove_by_prefix()
    {
        // given
        var sut = _CreateSut();

        _currentScope = "scope-a";
        await sut.UpsertAsync("perm:read", "a", _DefaultExpiration, AbortToken);
        await sut.UpsertAsync("perm:write", "a", _DefaultExpiration, AbortToken);

        _currentScope = "scope-b";
        await sut.UpsertAsync("perm:read", "b", _DefaultExpiration, AbortToken);

        // when — remove by prefix for scope-a only
        _currentScope = "scope-a";
        await sut.RemoveByPrefixAsync("perm:", AbortToken);

        // then
        _currentScope = "scope-a";
        (await sut.GetAsync("perm:read", AbortToken)).HasValue.Should().BeFalse();

        _currentScope = "scope-b";
        (await sut.GetAsync("perm:read", AbortToken)).HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task should_scope_get_by_prefix_and_unscope_returned_keys()
    {
        // given
        var sut = _CreateSut();

        _currentScope = "scope-a";
        await sut.UpsertAsync("ns:key1", "a1", _DefaultExpiration, AbortToken);
        await sut.UpsertAsync("ns:key2", "a2", _DefaultExpiration, AbortToken);
        await sut.UpsertAsync("other:key", "ax", _DefaultExpiration, AbortToken);

        // when
        var result = await sut.GetByPrefixAsync("ns:", AbortToken);

        // then — returned keys are unscoped
        result.Should().HaveCount(2);
        result.Should().ContainKey("ns:key1");
        result.Should().ContainKey("ns:key2");
    }
}
