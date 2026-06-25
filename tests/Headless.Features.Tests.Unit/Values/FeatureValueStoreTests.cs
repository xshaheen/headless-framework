// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Features.Values;
using Headless.Testing.Tests;

namespace Tests.Values;

public sealed class FeatureValueStoreTests : TestBase
{
    private readonly IFeatureDefinitionManager _definitionManager = Substitute.For<IFeatureDefinitionManager>();
    private readonly IFeatureValueRecordRepository _repository = Substitute.For<IFeatureValueRecordRepository>();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();
    private readonly ICache _cache = Substitute.For<ICache>();
    private readonly FeatureValueStore _sut;

    public FeatureValueStoreTests()
    {
        _sut = new FeatureValueStore(_definitionManager, _repository, _guidGenerator, _cache);
    }

    #region GetOrDefaultAsync

    [Fact]
    public async Task should_get_value_from_cache_without_hitting_repository()
    {
        // given
        const string name = "TestFeature";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        const string expectedValue = "cached-value";
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        _cache
            .GetAsync<FeatureValueCacheItem>(cacheKey, AbortToken)
            .Returns(new CacheValue<FeatureValueCacheItem>(new FeatureValueCacheItem(expectedValue), hasValue: true));

        // when
        var result = await _sut.GetOrDefaultAsync(name, providerName, providerKey, AbortToken);

        // then
        result.Should().Be(expectedValue);
        await _repository
            .DidNotReceive()
            .GetListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region SetAsync

    [Fact]
    public async Task should_insert_and_write_through_cache_when_value_is_new()
    {
        // given
        const string name = "TestFeature";
        const string newValue = "new-value";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        _repository.FindAsync(name, providerName, providerKey, AbortToken).Returns((FeatureValueRecord?)null);

        // when
        await _sut.SetAsync(name, newValue, providerName, providerKey, AbortToken);

        // then
        await _repository.Received(1).InsertAsync(Arg.Is<FeatureValueRecord>(r => r.Value == newValue), AbortToken);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<FeatureValueRecord>(), Arg.Any<CancellationToken>());
        await _cache
            .Received(1)
            .UpsertAsync(
                cacheKey,
                Arg.Is<FeatureValueCacheItem>(i => i.Value == newValue),
                Arg.Any<TimeSpan?>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_and_write_through_cache_when_value_exists()
    {
        // given
        const string name = "TestFeature";
        const string newValue = "new-value";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        var existing = new FeatureValueRecord(Guid.NewGuid(), name, "old-value", providerName, providerKey);

        _repository.FindAsync(name, providerName, providerKey, AbortToken).Returns(existing);

        // when
        await _sut.SetAsync(name, newValue, providerName, providerKey, AbortToken);

        // then
        await _repository.Received(1).UpdateAsync(existing, AbortToken);
        await _repository.DidNotReceive().InsertAsync(Arg.Any<FeatureValueRecord>(), Arg.Any<CancellationToken>());
        await _cache
            .Received(1)
            .UpsertAsync(
                cacheKey,
                Arg.Is<FeatureValueCacheItem>(i => i.Value == newValue),
                Arg.Any<TimeSpan?>(),
                AbortToken
            );
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task should_delete_records()
    {
        // given
        const string name = "TestFeature";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        List<FeatureValueRecord> records = [new(Guid.NewGuid(), name, "value", providerName, providerKey)];

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
        const string name = "TestFeature";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);
        List<FeatureValueRecord> records = [new(Guid.NewGuid(), name, "value", providerName, providerKey)];

        _repository.FindAllAsync(name, providerName, providerKey, AbortToken).Returns(records);

        // when
        await _sut.DeleteAsync(name, providerName, providerKey, AbortToken);

        // then
        await _cache.Received(1).RemoveAsync(cacheKey, AbortToken);
    }

    [Fact]
    public async Task should_not_delete_or_invalidate_when_no_records_found()
    {
        // given
        const string name = "TestFeature";
        const string providerName = "TestProvider";
        const string? providerKey = null;

        _repository.FindAllAsync(name, providerName, providerKey, AbortToken).Returns([]);

        // when
        await _sut.DeleteAsync(name, providerName, providerKey, AbortToken);

        // then
        await _repository
            .DidNotReceive()
            .DeleteAsync(Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
