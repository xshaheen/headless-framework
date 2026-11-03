// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Features;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Values;

/// <summary>
/// A reader that misses the cache loads the scope from the repository and fills the cache. A write that lands between
/// that load and the fill must win: the reader may return what it read, but must not leave it cached.
/// </summary>
public sealed class FeatureValueStoreConcurrentFillTests : TestBase
{
    private const string _Feature = "Reports.Export";
    private const string _ProviderName = "Tenant";
    private const string _ProviderKey = "acme";

    private readonly IFeatureDefinitionManager _definitionManager = Substitute.For<IFeatureDefinitionManager>();
    private readonly IFeatureValueRecordRepository _repository = Substitute.For<IFeatureValueRecordRepository>();
    private readonly InMemoryCache _cache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly FeatureValueStore _sut;

    private readonly TaskCompletionSource _readerLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly FeatureValueRecord _record = new(Guid.NewGuid(), _Feature, "true", _ProviderName, _ProviderKey);
    private string? _storedValue = "true";

    public FeatureValueStoreConcurrentFillTests()
    {
        IReadOnlyList<FeatureDefinition> definitions = [new(_Feature)];
        _definitionManager.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(definitions);

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        _sut = new FeatureValueStore(
            _definitionManager,
            _repository,
            guidGenerator,
            _cache,
            NullLogger<FeatureValueStore>.Instance
        );
    }

    [Fact]
    public async Task should_not_cache_the_value_it_read_when_a_set_lands_before_the_fill()
    {
        // given a reader paused between its repository load ("true") and its cache fill
        _PauseFirstScopeLoad();
        _repository
            .FindAsync(_Feature, _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _storedValue is null ? null : _record);
        _repository
            .UpdateAsync(Arg.Any<FeatureValueRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _storedValue = call.Arg<FeatureValueRecord>().Value;

                return Task.CompletedTask;
            });

        var reader = _sut.GetOrDefaultAsync(_Feature, _ProviderName, _ProviderKey, AbortToken);
        await _readerLoaded.Task.WaitAsync(AbortToken);

        // when a writer changes the value, then the reader finishes its fill
        await _sut.SetAsync(_Feature, "false", _ProviderName, _ProviderKey, AbortToken);
        _releaseReader.SetResult();
        await reader;

        // then the next read sees the write, not the reader's pre-write load
        (await _sut.GetOrDefaultAsync(_Feature, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be("false");
    }

    [Fact]
    public async Task should_not_cache_the_value_it_read_when_a_delete_lands_before_the_fill()
    {
        // given a reader paused between its repository load ("true") and its cache fill
        _PauseFirstScopeLoad();
        _repository
            .FindAllAsync(_Feature, _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _storedValue is null ? [] : [_record]);
        _repository
            .DeleteAsync(Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _storedValue = null;

                return Task.CompletedTask;
            });

        var reader = _sut.GetOrDefaultAsync(_Feature, _ProviderName, _ProviderKey, AbortToken);
        await _readerLoaded.Task.WaitAsync(AbortToken);

        // when a writer deletes the value (an eviction, not an overwrite), then the reader finishes its fill
        await _sut.DeleteAsync(_Feature, _ProviderName, _ProviderKey, AbortToken);
        _releaseReader.SetResult();
        await reader;

        // then the next read sees the delete
        (await _sut.GetOrDefaultAsync(_Feature, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task should_not_cache_the_batch_it_read_when_a_set_lands_before_the_fill()
    {
        // given a batch reader paused between its repository load and its cache fill
        const string other = "Reports.Print";
        IReadOnlyList<FeatureDefinition> definitions = [new(_Feature), new(other)];
        _definitionManager.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(definitions);

        var loads = 0;
        _repository
            .GetListAsync(Arg.Any<HashSet<string>>(), _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var snapshot = _Snapshot();

                if (Interlocked.Increment(ref loads) == 1)
                {
                    _readerLoaded.SetResult();
                    await _releaseReader.Task;
                }

                return snapshot;
            });
        _repository
            .SaveAsync(
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<IReadOnlyCollection<FeatureValueRecord>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _storedValue = call.ArgAt<IReadOnlyCollection<FeatureValueRecord>>(1).Single().Value;

                return Task.CompletedTask;
            });

        var reader = _sut.GetAllAsync([_Feature, other], _ProviderName, _ProviderKey, AbortToken);
        await _readerLoaded.Task.WaitAsync(AbortToken);

        // when a writer changes the value, then the reader finishes its fill
        await _sut.SetAllAsync(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [_Feature] = "false" },
            _ProviderName,
            _ProviderKey,
            AbortToken
        );
        _releaseReader.SetResult();
        await reader;

        // then the next read sees the write
        var values = await _sut.GetAllAsync([_Feature, other], _ProviderName, _ProviderKey, AbortToken);
        values.Should().ContainSingle(x => x.Name == _Feature).Which.Value.Should().Be("false");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        await base.DisposeAsyncCore();
    }

    /// <summary>The first scope load captures the stored value, then waits until the test lets it fill the cache.</summary>
    private void _PauseFirstScopeLoad()
    {
        var loads = 0;
        _repository
            .GetListAsync(_ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var snapshot = _Snapshot();

                if (Interlocked.Increment(ref loads) == 1)
                {
                    _readerLoaded.SetResult();
                    await _releaseReader.Task;
                }

                return snapshot;
            });
    }

    private List<FeatureValueRecord> _Snapshot()
    {
        return _storedValue is null
            ? []
            : [new FeatureValueRecord(_record.Id, _Feature, _storedValue, _ProviderName, _ProviderKey)];
    }
}
