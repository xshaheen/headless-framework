// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Features;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace Tests.Values;

public sealed class FeatureValueStoreUnitOfWorkTests : TestBase
{
    private const string _Provider = "Tenant";
    private const string _Key = "tenant-a";

    private readonly IFeatureValueRecordRepository _repository = Substitute.For<IFeatureValueRecordRepository>();
    private readonly ICache _cache = Substitute.For<ICache>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();
    private readonly List<Func<ValueTask>> _afterCommit = [];
    private readonly FeatureValueStore _sut;

    public FeatureValueStoreUnitOfWorkTests()
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        _unit.OnCompleted(Arg.Do<Func<ValueTask>>(_afterCommit.Add)).Returns(Substitute.For<IDisposable>());

        _sut = new FeatureValueStore(
            Substitute.For<IFeatureDefinitionManager>(),
            _repository,
            guidGenerator,
            _cache,
            NullLogger<FeatureValueStore>.Instance
        );
    }

    [Fact]
    public async Task should_plan_the_batch_inside_the_unit_and_clear_every_key_only_after_commit()
    {
        // given A has a row, B has none, and C is cleared without a row
        var existing = new FeatureValueRecord(Guid.NewGuid(), "A", "1", _Provider, _Key);
        _repository
            .GetListEnlistedAsync(_unit, Arg.Any<HashSet<string>>(), _Provider, _Key, AbortToken)
            .Returns([existing]);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["A"] = "2",
            ["B"] = "1",
            ["C"] = null,
        };

        // when
        await _sut.SetAllEnlistedAsync(_unit, values, _Provider, _Key, AbortToken);

        // then
        await _repository
            .Received(1)
            .SaveEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(x => x.Single().Name == "B"),
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(x => x.Single().Value == "2"),
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(x => x.Count == 0),
                AbortToken
            );
        _cache.ReceivedCalls().Should().BeEmpty();

        await _CommitAsync();
        await _cache
            .Received(1)
            .RemoveAllAsync(
                Arg.Is<IEnumerable<string>>(keys =>
                    keys.Order()
                        .SequenceEqual(
                            new[] { _CacheKey("A"), _CacheKey("B"), _CacheKey("C") }.Order(),
                            StringComparer.Ordinal
                        )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_try_an_enlisted_batch_once_and_leave_the_retry_to_the_unit()
    {
        // given
        _repository.GetListEnlistedAsync(_unit, Arg.Any<HashSet<string>>(), _Provider, _Key, AbortToken).Returns([]);
        _repository
            .SaveEnlistedAsync(
                _unit,
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                AbortToken
            )
            .ThrowsAsync(new InvalidOperationException("duplicate key"));
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "1" };

        // when
        var act = () => _sut.SetAllEnlistedAsync(_unit, values, _Provider, _Key, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("duplicate key");
        await _repository
            .Received(1)
            .GetListEnlistedAsync(_unit, Arg.Any<HashSet<string>>(), _Provider, _Key, AbortToken);
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_inside_the_unit_without_the_cache()
    {
        // given
        _repository
            .GetListEnlistedAsync(_unit, Arg.Any<HashSet<string>>(), _Provider, _Key, AbortToken)
            .Returns([new FeatureValueRecord(Guid.NewGuid(), "A", "written-in-unit", _Provider, _Key)]);

        // when
        var value = await _sut.GetOrDefaultEnlistedAsync(_unit, "A", _Provider, _Key, AbortToken);

        // then
        value.Should().Be("written-in-unit");
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_null_when_the_unit_sees_no_stored_value()
    {
        // given
        _repository.GetListEnlistedAsync(_unit, Arg.Any<HashSet<string>>(), _Provider, _Key, AbortToken).Returns([]);

        // when
        var value = await _sut.GetOrDefaultEnlistedAsync(_unit, "A", _Provider, _Key, AbortToken);

        // then
        value.Should().BeNull();
    }

    [Fact]
    public async Task should_delete_a_scope_inside_the_unit_and_return_the_removed_names()
    {
        // given
        List<FeatureValueRecord> records =
        [
            new(Guid.NewGuid(), "A", "1", _Provider, _Key),
            new(Guid.NewGuid(), "B", "2", _Provider, _Key),
        ];
        _repository.GetListEnlistedAsync(_unit, _Provider, _Key, AbortToken).Returns(records);

        // when
        var removed = await _sut.DeleteEnlistedAsync(_unit, _Provider, _Key, AbortToken);

        // then
        removed.Should().BeEquivalentTo("A", "B");
        await _repository
            .Received(1)
            .SaveEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(x => x.Count == 0),
                Arg.Is<IReadOnlyCollection<FeatureValueRecord>>(x => x.Count == 0),
                records,
                AbortToken
            );
        _cache.ReceivedCalls().Should().BeEmpty();
        await _CommitAsync();
        await _cache.Received(1).RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_register_nothing_when_an_enlisted_delete_finds_no_values()
    {
        // given
        _repository.GetListEnlistedAsync(_unit, _Provider, _Key, AbortToken).Returns([]);

        // when
        var removed = await _sut.DeleteEnlistedAsync(_unit, _Provider, _Key, AbortToken);

        // then
        removed.Should().BeEmpty();
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_log_rather_than_throw_when_the_cache_fails_after_commit()
    {
        // given
        _repository.GetListEnlistedAsync(_unit, Arg.Any<HashSet<string>>(), _Provider, _Key, AbortToken).Returns([]);
        _cache
            .RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "1" };
        await _sut.SetAllEnlistedAsync(_unit, values, _Provider, _Key, AbortToken);

        // when
        var act = _CommitAsync;

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_refuse_a_padded_key_before_enlisting()
    {
        // when
        var act = () =>
            _sut.SetAllEnlistedAsync(
                _unit,
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "1" },
                _Provider,
                "tenant-a ",
                AbortToken
            );

        // then
        await act.Should().ThrowExactlyAsync<ArgumentException>();
        _repository.ReceivedCalls().Should().BeEmpty();
    }

    private static string _CacheKey(string name)
    {
        return FeatureValueCacheItem.CalculateCacheKey(name, _Provider, _Key);
    }

    private async Task _CommitAsync()
    {
        foreach (var work in _afterCommit)
        {
            await work();
        }
    }
}
