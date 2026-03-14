// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Verifies <see cref="TenantCache{T}"/> provides tenant isolation via <c>t:{tenantId}</c> prefix.
/// Core scoping logic is covered by <see cref="ScopedCacheTests"/>.
/// </summary>
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
}
