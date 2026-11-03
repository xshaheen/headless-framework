// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Values;

/// <summary>
/// A reader that misses the cache loads the scope from the repository and fills the cache. A write that lands between
/// that load and the fill must win: the reader may return what it read, but must not leave it cached.
/// </summary>
public sealed class SettingValueStoreConcurrentFillTests : TestBase
{
    private const string _Setting = "Theme";
    private const string _ProviderName = "Tenant";
    private const string _ProviderKey = "acme";

    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly ISettingValueRecordRepository _repository = Substitute.For<ISettingValueRecordRepository>();
    private readonly InMemoryCache _memoryCache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly SettingValueStore _sut;

    private readonly TaskCompletionSource _readerLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseReader = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SettingValueRecord _record = new(Guid.NewGuid(), _Setting, "dark", _ProviderName, _ProviderKey);
    private string? _storedValue = "dark";

    public SettingValueStoreConcurrentFillTests()
    {
        IReadOnlyList<SettingDefinition> definitions = [new(_Setting)];
        _definitionManager.GetAllAsync(Arg.Any<CancellationToken>()).Returns(definitions);

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        _sut = new SettingValueStore(
            _repository,
            _definitionManager,
            guidGenerator,
            new Cache<SettingValueCacheItem>(_memoryCache),
            Options.Create(new SettingManagementOptions { ValueCacheExpiration = TimeSpan.FromHours(5) }),
            NullLogger<SettingValueStore>.Instance
        );
    }

    [Fact]
    public async Task should_not_cache_the_value_it_read_when_a_set_lands_before_the_fill()
    {
        // given a reader paused between its repository load ("dark") and its cache fill
        _PauseFirstScopeLoad();
        _repository
            .FindAsync(_Setting, _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _storedValue is null ? null : _record);
        _repository
            .UpdateAsync(Arg.Any<SettingValueRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _storedValue = call.Arg<SettingValueRecord>().Value;

                return Task.CompletedTask;
            });

        var reader = _sut.GetOrDefaultAsync(_Setting, _ProviderName, _ProviderKey, AbortToken);
        await _readerLoaded.Task.WaitAsync(AbortToken);

        // when a writer changes the value, then the reader finishes its fill
        await _sut.SetAsync(_Setting, "light", _ProviderName, _ProviderKey, AbortToken);
        _releaseReader.SetResult();
        await reader;

        // then the next read sees the write, not the reader's pre-write load
        (await _sut.GetOrDefaultAsync(_Setting, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be("light");
    }

    [Fact]
    public async Task should_not_cache_the_value_it_read_when_a_delete_lands_before_the_fill()
    {
        // given a reader paused between its repository load ("dark") and its cache fill
        _PauseFirstScopeLoad();
        _repository
            .FindAllAsync(_Setting, _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _storedValue is null ? [] : [_record]);
        _repository
            .DeleteAsync(Arg.Any<IReadOnlyCollection<SettingValueRecord>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _storedValue = null;

                return Task.CompletedTask;
            });

        var reader = _sut.GetOrDefaultAsync(_Setting, _ProviderName, _ProviderKey, AbortToken);
        await _readerLoaded.Task.WaitAsync(AbortToken);

        // when a writer deletes the value (an eviction, not an overwrite), then the reader finishes its fill
        await _sut.DeleteAsync(_Setting, _ProviderName, _ProviderKey, AbortToken);
        _releaseReader.SetResult();
        await reader;

        // then the next read sees the delete
        (await _sut.GetOrDefaultAsync(_Setting, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task should_not_cache_the_batch_it_read_when_a_set_lands_before_the_fill()
    {
        // given a batch reader paused between its repository load and its cache fill
        const string other = "Language";
        IReadOnlyList<SettingDefinition> definitions = [new(_Setting), new(other)];
        _definitionManager.GetAllAsync(Arg.Any<CancellationToken>()).Returns(definitions);

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
                Arg.Any<IReadOnlyCollection<SettingValueRecord>>(),
                Arg.Any<IReadOnlyCollection<SettingValueRecord>>(),
                Arg.Any<IReadOnlyCollection<SettingValueRecord>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _storedValue = call.ArgAt<IReadOnlyCollection<SettingValueRecord>>(1).Single().Value;

                return Task.CompletedTask;
            });

        var reader = _sut.GetAllAsync([_Setting, other], _ProviderName, _ProviderKey, AbortToken);
        await _readerLoaded.Task.WaitAsync(AbortToken);

        // when a writer changes the value, then the reader finishes its fill
        await _sut.SetAllAsync(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [_Setting] = "light" },
            _ProviderName,
            _ProviderKey,
            AbortToken
        );
        _releaseReader.SetResult();
        await reader;

        // then the next read sees the write
        var values = await _sut.GetAllAsync([_Setting, other], _ProviderName, _ProviderKey, AbortToken);
        values.Should().ContainSingle(x => x.Name == _Setting).Which.Value.Should().Be("light");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _memoryCache.Dispose();
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

    private List<SettingValueRecord> _Snapshot()
    {
        return _storedValue is null
            ? []
            : [new SettingValueRecord(_record.Id, _Setting, _storedValue, _ProviderName, _ProviderKey)];
    }
}
