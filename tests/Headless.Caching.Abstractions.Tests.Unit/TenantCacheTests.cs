// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class TenantCacheTests : TestBase
{
    private static readonly TimeSpan _DefaultExpiration = TimeSpan.FromMinutes(5);
    private readonly FakeTimeProvider _timeProvider = new();
    private string? _currentTenantId;

    private TenantCache<string> _CreateSut()
    {
        var cache = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        return new TenantCache<string>(cache, () => _currentTenantId);
    }

    [Fact]
    public async Task should_isolate_cache_entries_by_tenant()
    {
        // given
        var sut = _CreateSut();

        _currentTenantId = "tenant-a";
        await sut.UpsertAsync("key", "value-a", _DefaultExpiration, AbortToken);

        _currentTenantId = "tenant-b";
        await sut.UpsertAsync("key", "value-b", _DefaultExpiration, AbortToken);

        // when / then
        _currentTenantId = "tenant-a";
        var resultA = await sut.GetAsync("key", AbortToken);
        resultA.HasValue.Should().BeTrue();
        resultA.Value.Should().Be("value-a");

        _currentTenantId = "tenant-b";
        var resultB = await sut.GetAsync("key", AbortToken);
        resultB.HasValue.Should().BeTrue();
        resultB.Value.Should().Be("value-b");
    }

    [Fact]
    public async Task should_not_find_key_from_different_tenant()
    {
        // given
        var sut = _CreateSut();

        _currentTenantId = "tenant-a";
        await sut.UpsertAsync("key", "value", _DefaultExpiration, AbortToken);

        // when
        _currentTenantId = "tenant-b";
        var result = await sut.GetAsync("key", AbortToken);

        // then
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_handle_null_tenant_id()
    {
        // given
        var sut = _CreateSut();

        _currentTenantId = null;
        await sut.UpsertAsync("key", "host-value", _DefaultExpiration, AbortToken);

        // when
        _currentTenantId = null;
        var hostResult = await sut.GetAsync("key", AbortToken);

        _currentTenantId = "tenant-a";
        var tenantResult = await sut.GetAsync("key", AbortToken);

        // then
        hostResult.HasValue.Should().BeTrue();
        hostResult.Value.Should().Be("host-value");
        tenantResult.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_only_for_current_tenant()
    {
        // given
        var sut = _CreateSut();

        _currentTenantId = "tenant-a";
        await sut.UpsertAsync("key", "value-a", _DefaultExpiration, AbortToken);

        _currentTenantId = "tenant-b";
        await sut.UpsertAsync("key", "value-b", _DefaultExpiration, AbortToken);

        // when — remove from tenant-a
        _currentTenantId = "tenant-a";
        await sut.RemoveAsync("key", AbortToken);

        // then — tenant-b unaffected
        _currentTenantId = "tenant-a";
        var resultA = await sut.GetAsync("key", AbortToken);
        resultA.HasValue.Should().BeFalse();

        _currentTenantId = "tenant-b";
        var resultB = await sut.GetAsync("key", AbortToken);
        resultB.HasValue.Should().BeTrue();
        resultB.Value.Should().Be("value-b");
    }

    [Fact]
    public async Task should_scope_get_all_and_unscope_returned_keys()
    {
        // given
        var sut = _CreateSut();

        _currentTenantId = "tenant-a";
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

        _currentTenantId = "tenant-a";
        await sut.UpsertAllAsync(values, _DefaultExpiration, AbortToken);

        // when — different tenant sees nothing
        _currentTenantId = "tenant-b";
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

        _currentTenantId = "tenant-a";
        await sut.UpsertAsync("perm:read", "a", _DefaultExpiration, AbortToken);
        await sut.UpsertAsync("perm:write", "a", _DefaultExpiration, AbortToken);

        _currentTenantId = "tenant-b";
        await sut.UpsertAsync("perm:read", "b", _DefaultExpiration, AbortToken);

        // when — remove by prefix for tenant-a only
        _currentTenantId = "tenant-a";
        await sut.RemoveByPrefixAsync("perm:", AbortToken);

        // then
        _currentTenantId = "tenant-a";
        (await sut.GetAsync("perm:read", AbortToken)).HasValue.Should().BeFalse();

        _currentTenantId = "tenant-b";
        (await sut.GetAsync("perm:read", AbortToken)).HasValue.Should().BeTrue();
    }
}
