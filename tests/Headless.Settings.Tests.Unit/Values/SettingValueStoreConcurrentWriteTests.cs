// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Values;

/// <summary>
/// Two writers of one value commit in one order and can reach the cache in the other. The cache must end at the value
/// the repository kept, the last commit, not at whichever writer touched the cache last. Each test runs the second
/// writer from inside the first writer's commit, after its row lands and before it returns to update the cache.
/// </summary>
public sealed class SettingValueStoreConcurrentWriteTests : TestBase
{
    private const string _Setting = "Theme";
    private const string _ProviderName = "Tenant";
    private const string _ProviderKey = "acme";

    private readonly ISettingValueRecordRepository _repository = Substitute.For<ISettingValueRecordRepository>();
    private readonly InMemoryCache _memoryCache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly Guid _recordId = Guid.NewGuid();
    private readonly SettingValueStore _sut;

    private string? _storedValue = "light";

    public SettingValueStoreConcurrentWriteTests()
    {
        var definitionManager = Substitute.For<ISettingDefinitionManager>();
        IReadOnlyList<SettingDefinition> definitions = [new(_Setting)];
        definitionManager.GetAllAsync(Arg.Any<CancellationToken>()).Returns(definitions);

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        // Every read returns a fresh record, as a database would, so the two writers never share a tracked instance.
        _repository
            .FindAsync(_Setting, _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _Snapshot().FirstOrDefault());
        _repository.GetListAsync(_ProviderName, _ProviderKey, Arg.Any<CancellationToken>()).Returns(_ => _Snapshot());
        _repository
            .GetListAsync(Arg.Any<HashSet<string>>(), _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _Snapshot());

        _sut = new SettingValueStore(
            _repository,
            definitionManager,
            guidGenerator,
            new Cache<SettingValueCacheItem>(_memoryCache),
            Options.Create(new SettingManagementOptions { ValueCacheExpiration = TimeSpan.FromHours(5) }),
            NullLogger<SettingValueStore>.Instance
        );
    }

    [Fact]
    public async Task should_cache_the_last_committed_value_when_the_first_writer_updates_the_cache_last()
    {
        // given a first writer whose commit lets a second writer commit and update the cache before it returns
        var updates = 0;
        _repository
            .UpdateAsync(Arg.Any<SettingValueRecord>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _storedValue = call.Arg<SettingValueRecord>().Value;

                if (Interlocked.Increment(ref updates) == 1)
                {
                    await _sut.SetAsync(_Setting, "dark", _ProviderName, _ProviderKey, AbortToken);
                }
            });

        // when the first writer then updates the cache
        await _sut.SetAsync(_Setting, "blue", _ProviderName, _ProviderKey, AbortToken);

        // then the cache holds what the repository kept, the second writer's value
        _storedValue.Should().Be("dark");
        (await _sut.GetOrDefaultAsync(_Setting, _ProviderName, _ProviderKey, AbortToken)).Should().Be("dark");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _memoryCache.Dispose();
        await base.DisposeAsyncCore();
    }

    private List<SettingValueRecord> _Snapshot()
    {
        return _storedValue is null
            ? []
            : [new SettingValueRecord(_recordId, _Setting, _storedValue, _ProviderName, _ProviderKey)];
    }
}
