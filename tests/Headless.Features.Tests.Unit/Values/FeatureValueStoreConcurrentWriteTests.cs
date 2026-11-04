// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Features;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Values;

/// <summary>
/// Two writers of one value commit in one order and can reach the cache in the other. The cache must end at the value
/// the repository kept, the last commit, not at whichever writer touched the cache last. Each test runs the second
/// writer from inside the first writer's commit, after its row lands and before it returns to update the cache.
/// </summary>
public sealed class FeatureValueStoreConcurrentWriteTests : TestBase
{
    private const string _Feature = "Reports.Export";
    private const string _ProviderName = "Tenant";
    private const string _ProviderKey = "acme";

    private readonly IFeatureValueRecordRepository _repository = Substitute.For<IFeatureValueRecordRepository>();
    private readonly InMemoryCache _cache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly Guid _recordId = Guid.NewGuid();
    private readonly FeatureValueStore _sut;

    private string? _storedValue = "initial";

    public FeatureValueStoreConcurrentWriteTests()
    {
        var definitionManager = Substitute.For<IFeatureDefinitionManager>();
        IReadOnlyList<FeatureDefinition> definitions = [new(_Feature)];
        definitionManager.GetFeaturesAsync(Arg.Any<CancellationToken>()).Returns(definitions);

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        // Every read returns a fresh record, as a database would, so the two writers never share a tracked instance.
        _repository
            .FindAsync(_Feature, _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _Snapshot().FirstOrDefault());
        _repository.GetListAsync(_ProviderName, _ProviderKey, Arg.Any<CancellationToken>()).Returns(_ => _Snapshot());
        _repository
            .GetListAsync(Arg.Any<HashSet<string>>(), _ProviderName, _ProviderKey, Arg.Any<CancellationToken>())
            .Returns(_ => _Snapshot());

        _sut = new FeatureValueStore(
            definitionManager,
            _repository,
            guidGenerator,
            _cache,
            NullLogger<FeatureValueStore>.Instance
        );
    }

    [Fact]
    public async Task should_cache_the_last_committed_value_when_the_first_writer_updates_the_cache_last()
    {
        // given a first writer whose commit lets a second writer commit and update the cache before it returns
        var updates = 0;
        _repository
            .UpdateAsync(Arg.Any<FeatureValueRecord>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _storedValue = call.Arg<FeatureValueRecord>().Value;

                if (Interlocked.Increment(ref updates) == 1)
                {
                    await _sut.SetAsync(_Feature, "second", _ProviderName, _ProviderKey, AbortToken);
                }
            });

        // when the first writer then updates the cache
        await _sut.SetAsync(_Feature, "first", _ProviderName, _ProviderKey, AbortToken);

        // then the cache holds what the repository kept, the second writer's value
        _storedValue.Should().Be("second");
        (await _sut.GetOrDefaultAsync(_Feature, _ProviderName, _ProviderKey, AbortToken)).Should().Be("second");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        await base.DisposeAsyncCore();
    }

    private List<FeatureValueRecord> _Snapshot()
    {
        return _storedValue is null
            ? []
            : [new FeatureValueRecord(_recordId, _Feature, _storedValue, _ProviderName, _ProviderKey)];
    }
}
