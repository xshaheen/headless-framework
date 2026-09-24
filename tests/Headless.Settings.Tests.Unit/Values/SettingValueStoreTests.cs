// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Settings.Definitions;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.Repositories;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

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
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        const string expectedValue = "cached-value";
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
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        const string expectedValue = "repo-value";
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
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        const string expectedValue = "repo-value";
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

    #region SetAllAsync

    [Fact]
    public async Task should_apply_a_batch_as_one_repository_change_set_and_refresh_the_cache()
    {
        // given the scope stores A and B; the batch updates A, clears B, adds C, and clears D which was never stored
        const string providerName = "TestProvider";
        const string providerKey = "tenant-1";
        var existingA = new SettingValueRecord(Guid.NewGuid(), "A", "old-a", providerName, providerKey);
        var existingB = new SettingValueRecord(Guid.NewGuid(), "B", "old-b", providerName, providerKey);
        _repository
            .GetListAsync(Arg.Any<HashSet<string>>(), providerName, providerKey, AbortToken)
            .Returns([existingA, existingB]);
        _guidGenerator.Create().Returns(Guid.NewGuid());
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["A"] = "new-a",
            ["B"] = null,
            ["C"] = "new-c",
            ["D"] = null,
        };

        // when
        await _sut.SetAllAsync(values, providerName, providerKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .GetListAsync(
                Arg.Is<HashSet<string>>(names => names.SetEquals(new[] { "A", "B", "C", "D" })),
                providerName,
                providerKey,
                AbortToken
            );
        await _repository
            .Received(1)
            .SaveAsync(
                Arg.Is<IReadOnlyCollection<SettingValueRecord>>(inserted =>
                    inserted.Count == 1 && inserted.Single().Name == "C" && inserted.Single().Value == "new-c"
                ),
                Arg.Is<IReadOnlyCollection<SettingValueRecord>>(updated =>
                    updated.Count == 1 && updated.Single() == existingA && existingA.Value == "new-a"
                ),
                Arg.Is<IReadOnlyCollection<SettingValueRecord>>(deleted =>
                    deleted.Count == 1 && deleted.Single() == existingB
                ),
                AbortToken
            );
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, SettingValueCacheItem>>(items =>
                    items.Count == 2
                    && items[SettingValueCacheItem.CalculateCacheKey("A", providerName, providerKey)].Value == "new-a"
                    && items[SettingValueCacheItem.CalculateCacheKey("C", providerName, providerKey)].Value == "new-c"
                ),
                Arg.Any<TimeSpan?>(),
                AbortToken
            );
        await _cache
            .Received(1)
            .RemoveAllAsync(
                Arg.Is<IEnumerable<string>>(keys =>
                    keys.SequenceEqual(
                        new[]
                        {
                            SettingValueCacheItem.CalculateCacheKey("B", providerName, providerKey),
                            SettingValueCacheItem.CalculateCacheKey("D", providerName, providerKey),
                        }
                    )
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_leave_the_cache_untouched_when_the_repository_rejects_the_batch()
    {
        // given
        const string providerName = "TestProvider";
        _repository.GetListAsync(Arg.Any<HashSet<string>>(), providerName, null, AbortToken).Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());
        _repository
            .SaveAsync(
                Arg.Any<IReadOnlyCollection<SettingValueRecord>>(),
                Arg.Any<IReadOnlyCollection<SettingValueRecord>>(),
                Arg.Any<IReadOnlyCollection<SettingValueRecord>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("write failed"));
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "a", ["B"] = null };

        // when
        var act = async () => await _sut.SetAllAsync(values, providerName, null, AbortToken);

        // then a reader keeps seeing the values the store still holds
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _cache
            .DidNotReceive()
            .UpsertAllAsync(
                Arg.Any<IDictionary<string, SettingValueCacheItem>>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        await _cache.DidNotReceive().RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task should_delete_value()
    {
        // given
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string? providerKey = null;
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
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string? providerKey = null;
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
        const string name = "TestSetting";
        const string providerName = "TestProvider";
        const string? providerKey = null;

        _repository.FindAllAsync(name, providerName, providerKey, AbortToken).Returns([]);

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
        const string providerName = "TestProvider";
        const string? providerKey = "tenant-123";
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
