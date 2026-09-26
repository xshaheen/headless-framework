// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Features.Definitions;
using Headless.Features.Entities;
using Headless.Features.Models;
using Headless.Features.Repositories;
using Headless.Features.Values;
using Headless.Testing.Tests;
using NSubstitute.ExceptionExtensions;

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

    [Fact]
    public async Task should_load_from_repository_and_cache_all_defined_features_on_miss()
    {
        // given
        const string providerName = "TestProvider";
        const string? providerKey = "tenant-1";
        const string requested = "FeatureA";
        var requestedKey = FeatureValueCacheItem.CalculateCacheKey(requested, providerName, providerKey);
        var otherKey = FeatureValueCacheItem.CalculateCacheKey("FeatureB", providerName, providerKey);

        _cache
            .GetAsync<FeatureValueCacheItem>(requestedKey, AbortToken)
            .Returns(CacheValue<FeatureValueCacheItem>.NoValue);

        IReadOnlyList<FeatureDefinition> definitions = [new("FeatureA"), new("FeatureB")];
        _definitionManager.GetFeaturesAsync(AbortToken).Returns(definitions);

        List<FeatureValueRecord> records =
        [
            new(Guid.NewGuid(), "FeatureA", "value-a", providerName, providerKey),
            new(Guid.NewGuid(), "FeatureB", "value-b", providerName, providerKey),
        ];
        _repository.GetListAsync(providerName, providerKey, AbortToken).Returns(records);

        // when
        var result = await _sut.GetOrDefaultAsync(requested, providerName, providerKey, AbortToken);

        // then — the requested value is returned, and *all* defined features for the scope are cached in one shot
        result.Should().Be("value-a");
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, FeatureValueCacheItem>>(d =>
                    d.Count == 2 && d[requestedKey].Value == "value-a" && d[otherKey].Value == "value-b"
                ),
                Arg.Any<TimeSpan?>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_cache_null_when_defined_feature_has_no_stored_value()
    {
        // given
        const string name = "TestFeature";
        const string providerName = "TestProvider";
        const string? providerKey = null;
        var cacheKey = FeatureValueCacheItem.CalculateCacheKey(name, providerName, providerKey);

        _cache.GetAsync<FeatureValueCacheItem>(cacheKey, AbortToken).Returns(CacheValue<FeatureValueCacheItem>.NoValue);

        IReadOnlyList<FeatureDefinition> definitions = [new(name)];
        _definitionManager.GetFeaturesAsync(AbortToken).Returns(definitions);
        _repository.GetListAsync(providerName, providerKey, AbortToken).Returns([]);

        // when
        var result = await _sut.GetOrDefaultAsync(name, providerName, providerKey, AbortToken);

        // then — a defined-but-unset feature caches a null marker so repeat reads stay cache-served
        result.Should().BeNull();
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, FeatureValueCacheItem>>(d =>
                    d.ContainsKey(cacheKey) && d[cacheKey].Value == null
                ),
                Arg.Any<TimeSpan?>(),
                AbortToken
            );
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

    #region SetAllAsync

    [Fact]
    public async Task should_apply_a_batch_as_one_repository_change_set_and_refresh_the_cache()
    {
        // given the scope stores A and B; the batch updates A, clears B, adds C, and clears D which was never stored
        const string providerName = "TestProvider";
        const string providerKey = "tenant-1";
        var existingA = new FeatureValueRecord(Guid.NewGuid(), "A", "old-a", providerName, providerKey);
        var existingB = new FeatureValueRecord(Guid.NewGuid(), "B", "old-b", providerName, providerKey);
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
            .DidNotReceive()
            .GetListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _repository
            .Received(1)
            .SaveAsync(
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(inserted =>
                    inserted.Count == 1 && inserted.Single().Name == "C" && inserted.Single().Value == "new-c"
                ),
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(updated =>
                    updated.Count == 1 && updated.Single() == existingA && existingA.Value == "new-a"
                ),
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(deleted =>
                    deleted.Count == 1 && deleted.Single() == existingB
                ),
                AbortToken
            );
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, FeatureValueCacheItem>>(items =>
                    items.Count == 2
                    && items[FeatureValueCacheItem.CalculateCacheKey("A", providerName, providerKey)].Value == "new-a"
                    && items[FeatureValueCacheItem.CalculateCacheKey("C", providerName, providerKey)].Value == "new-c"
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
                            FeatureValueCacheItem.CalculateCacheKey("B", providerName, providerKey),
                            FeatureValueCacheItem.CalculateCacheKey("D", providerName, providerKey),
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
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
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
                Arg.Any<IDictionary<string, FeatureValueCacheItem>>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        await _cache.DidNotReceive().RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_plan_again_and_update_when_a_concurrent_writer_inserted_the_row()
    {
        // given the first read finds nothing, but another writer inserts the row before this batch saves
        const string providerName = "TestProvider";
        const string providerKey = "tenant-1";
        var concurrent = new FeatureValueRecord(Guid.NewGuid(), "A", "theirs", providerName, providerKey);
        _repository
            .GetListAsync(Arg.Any<HashSet<string>>(), providerName, providerKey, AbortToken)
            .Returns([], [concurrent], [concurrent]);
        _guidGenerator.Create().Returns(Guid.NewGuid());
        var saves = new List<(int Inserted, int Updated)>();
        _repository
            .SaveAsync(
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                AbortToken
            )
            .Returns(call =>
            {
                saves.Add(
                    (
                        call.ArgAt<IReadOnlyCollection<FeatureValueRecord>>(0).Count,
                        call.ArgAt<IReadOnlyCollection<FeatureValueRecord>>(1).Count
                    )
                );

                return saves.Count == 1
                    ? Task.FromException(new InvalidOperationException("duplicate key"))
                    : Task.CompletedTask;
            });
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "ours" };

        // when
        await _sut.SetAllAsync(values, providerName, providerKey, AbortToken);

        // then the retry updates the row the other writer created, and the last writer's value wins
        saves.Should().Equal((1, 0), (0, 1));
        concurrent.Value.Should().Be("ours");
    }

    [Fact]
    public async Task should_rethrow_a_save_failure_when_no_other_writer_changed_the_rows()
    {
        // given
        const string providerName = "TestProvider";
        _repository.GetListAsync(Arg.Any<HashSet<string>>(), providerName, null, AbortToken).Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());
        _repository
            .SaveAsync(
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("value too long"));
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "a" };

        // when
        var act = async () => await _sut.SetAllAsync(values, providerName, null, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("value too long");
        await _repository
            .Received(1)
            .SaveAsync(
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<CancellationToken>()
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

    #region Surrounding white space

    // SQL Server ignores trailing spaces when comparing keys and PostgreSQL does not, so a padded key part would
    // address another key's row on one provider only; the store must refuse it before any storage call.
    public static readonly TheoryData<string> PaddedKeyCalls =
    [
        "get-name",
        "get-provider-name",
        "get-provider-key",
        "get-leading-provider-key",
        "set-name",
        "set-provider-key",
        "set-all-names",
        "set-all-provider-key",
        "delete-provider-key",
    ];

    [Theory]
    [MemberData(nameof(PaddedKeyCalls))]
    public async Task should_refuse_key_with_surrounding_white_space_before_touching_storage(string call)
    {
        // when
        Func<Task> action = call switch
        {
            "get-name" => async () => await _sut.GetOrDefaultAsync("Reports ", "Tenant", "acme", AbortToken),
            "get-provider-name" => async () => await _sut.GetOrDefaultAsync("Reports", "Tenant ", "acme", AbortToken),
            "get-provider-key" => async () => await _sut.GetOrDefaultAsync("Reports", "Tenant", "acme ", AbortToken),
            "get-leading-provider-key" => async () =>
                await _sut.GetOrDefaultAsync("Reports", "Tenant", " acme", AbortToken),
            "set-name" => async () => await _sut.SetAsync("Reports ", "true", "Tenant", "acme", AbortToken),
            "set-provider-key" => async () => await _sut.SetAsync("Reports", "true", "Tenant", "acme ", AbortToken),
            "set-all-names" => async () =>
                await _sut.SetAllAsync(
                    new Dictionary<string, string?> { ["Reports "] = "true" },
                    "Tenant",
                    "acme",
                    AbortToken
                ),
            "set-all-provider-key" => async () =>
                await _sut.SetAllAsync(
                    new Dictionary<string, string?> { ["Reports"] = "true" },
                    "Tenant",
                    "acme ",
                    AbortToken
                ),
            "delete-provider-key" => async () => await _sut.DeleteAsync("Reports", "Tenant", "acme ", AbortToken),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, null),
        };

        // then
        await action
            .Should()
            .ThrowExactlyAsync<ArgumentException>()
            .WithMessage("*must not start or end with white space*");
        _repository.ReceivedCalls().Should().BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    #endregion
}
