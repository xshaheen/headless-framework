// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Caching;
using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Framework.Settings.Repositories;
using Framework.Settings.Values;
using Framework.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Tests.Values;

public sealed class SettingValueStoreTests : TestBase
{
    private readonly ISettingValueRecordRepository _repository = Substitute.For<ISettingValueRecordRepository>();
    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();
    private readonly ICache<SettingValueCacheItem> _cache = Substitute.For<ICache<SettingValueCacheItem>>();
    private readonly SettingValueStore _sut;

    public SettingValueStoreTests()
    {
        var options = Options.Create(new SettingManagementOptions { ValueCacheExpiration = TimeSpan.FromHours(5) });
        _sut = new SettingValueStore(_repository, _definitionManager, _guidGenerator, _cache, options);
    }

    #region GetOrDefaultAsync

    [Fact]
    public async Task should_get_value_from_cache()
    {
        // given
        var name = "TestSetting";
        var providerName = "TestProvider";
        string? providerKey = null;
        var expectedValue = "cached-value";
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<SettingValueCacheItem>(new SettingValueCacheItem(expectedValue), hasValue: true));

        // when
        var result = await _sut.GetOrDefaultAsync(name, providerName, providerKey, AbortToken);

        // then
        result.Should().Be(expectedValue);
        await _repository
            .DidNotReceive()
            .GetListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_get_value_from_repository()
    {
        // given
        var name = "TestSetting";
        var providerName = "TestProvider";
        string? providerKey = null;
        var expectedValue = "repo-value";
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        _cache.GetAsync(cacheKey, AbortToken).Returns(CacheValue<SettingValueCacheItem>.NoValue);

        List<SettingDefinition> definitions = [new(name)];
        _definitionManager.GetAllAsync(AbortToken).Returns(definitions);

        List<SettingValueRecord> records = [new(Guid.NewGuid(), name, expectedValue, providerName, providerKey)];
        _repository.GetListAsync(providerName, providerKey, AbortToken).Returns(records);

        // when
        var result = await _sut.GetOrDefaultAsync(name, providerName, providerKey, AbortToken);

        // then
        result.Should().Be(expectedValue);
    }

    [Fact]
    public async Task should_cache_value_after_retrieval()
    {
        // given
        var name = "TestSetting";
        var providerName = "TestProvider";
        string? providerKey = null;
        var expectedValue = "repo-value";
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        _cache.GetAsync(cacheKey, AbortToken).Returns(CacheValue<SettingValueCacheItem>.NoValue);

        List<SettingDefinition> definitions = [new(name)];
        _definitionManager.GetAllAsync(AbortToken).Returns(definitions);

        List<SettingValueRecord> records = [new(Guid.NewGuid(), name, expectedValue, providerName, providerKey)];
        _repository.GetListAsync(providerName, providerKey, AbortToken).Returns(records);

        // when
        await _sut.GetOrDefaultAsync(name, providerName, providerKey, AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, SettingValueCacheItem>>(d =>
                    d.ContainsKey(cacheKey) && d[cacheKey].Value == expectedValue
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task should_delete_value()
    {
        // given
        var name = "TestSetting";
        var providerName = "TestProvider";
        string? providerKey = null;
        var recordId = Guid.NewGuid();
        List<SettingValueRecord> records = [new(recordId, name, "value", providerName, providerKey)];

        _repository.FindAllAsync(name, providerName, providerKey, AbortToken).Returns(records);

        // when
        await _sut.DeleteAsync(name, providerName, providerKey, AbortToken);

        // then
        await _repository.Received(1).DeleteAsync(records, AbortToken);
    }

    [Fact]
    public async Task should_invalidate_cache_on_delete()
    {
        // given
        var name = "TestSetting";
        var providerName = "TestProvider";
        string? providerKey = null;
        var cacheKey = SettingValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        List<SettingValueRecord> records = [new(Guid.NewGuid(), name, "value", providerName, providerKey)];

        _repository.FindAllAsync(name, providerName, providerKey, AbortToken).Returns(records);

        // when
        await _sut.DeleteAsync(name, providerName, providerKey, AbortToken);

        // then
        await _cache.Received(1).RemoveAsync(cacheKey, AbortToken);
    }

    [Fact]
    public async Task should_not_delete_when_no_records_found()
    {
        // given
        var name = "TestSetting";
        var providerName = "TestProvider";
        string? providerKey = null;

        _repository.FindAllAsync(name, providerName, providerKey, AbortToken).Returns(new List<SettingValueRecord>());

        // when
        await _sut.DeleteAsync(name, providerName, providerKey, AbortToken);

        // then
        await _repository
            .DidNotReceive()
            .DeleteAsync(Arg.Any<IReadOnlyCollection<SettingValueRecord>>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetAllProviderValuesAsync

    [Fact]
    public async Task should_get_all_provider_values()
    {
        // given
        var providerName = "TestProvider";
        string? providerKey = "tenant-123";
        List<SettingValueRecord> records =
        [
            new(Guid.NewGuid(), "Setting1", "value1", providerName, providerKey),
            new(Guid.NewGuid(), "Setting2", "value2", providerName, providerKey),
        ];

        _repository.GetListAsync(providerName, providerKey, AbortToken).Returns(records);

        // when
        var result = await _sut.GetAllProviderValuesAsync(providerName, providerKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(sv => sv.Name == "Setting1" && sv.Value == "value1");
        result.Should().Contain(sv => sv.Name == "Setting2" && sv.Value == "value2");
    }

    #endregion
}
