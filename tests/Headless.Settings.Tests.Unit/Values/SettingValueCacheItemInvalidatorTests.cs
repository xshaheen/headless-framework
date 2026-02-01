// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Domain;
using Headless.Settings.Entities;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Values;

public sealed class SettingValueCacheItemInvalidatorTests : TestBase
{
    private readonly ICache<SettingValueCacheItem> _cache = Substitute.For<ICache<SettingValueCacheItem>>();
    private readonly SettingValueCacheItemInvalidator _sut;

    public SettingValueCacheItemInvalidatorTests()
    {
        _sut = new SettingValueCacheItemInvalidator(_cache);
    }

    [Fact]
    public async Task should_invalidate_cache_on_event()
    {
        // given
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string providerKey = "tenant-123";
        var record = new SettingValueRecord(Guid.NewGuid(), name, "value", providerName, providerKey);
        var eventData = new EntityChangedEventData<SettingValueRecord>(record);
        var expectedCacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        // when
        await _sut.HandleAsync(eventData, AbortToken);

        // then
        await _cache.Received(1).RemoveAsync(expectedCacheKey, AbortToken);
    }

    [Fact]
    public async Task should_build_correct_cache_key()
    {
        // given
        const string name = "MySetting";
        const string providerName = "GlobalProvider";
        const string? providerKey = null;
        var record = new SettingValueRecord(Guid.NewGuid(), name, "value", providerName, providerKey);
        var eventData = new EntityChangedEventData<SettingValueRecord>(record);
        var expectedCacheKey = $"settings:provider:{providerName}:{providerKey},name:{name}";

        // when
        await _sut.HandleAsync(eventData, AbortToken);

        // then
        await _cache.Received(1).RemoveAsync(expectedCacheKey, AbortToken);
    }
}
