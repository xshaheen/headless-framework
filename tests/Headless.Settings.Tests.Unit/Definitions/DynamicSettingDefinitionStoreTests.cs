// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Context;
using Headless.DistributedLocks;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Definitions;

public sealed class DynamicSettingDefinitionStoreTests : TestBase
{
    private readonly ISettingDefinitionRecordRepository _repository =
        Substitute.For<ISettingDefinitionRecordRepository>();

    private readonly IStaticSettingDefinitionStore _staticStore = Substitute.For<IStaticSettingDefinitionStore>();
    private readonly ISettingDefinitionSerializer _serializer = Substitute.For<ISettingDefinitionSerializer>();
    private readonly ICache _cache = Substitute.For<ICache>();
    private readonly IDistributedLock _distributedLockProvider = Substitute.For<IDistributedLock>();

    private readonly SettingManagementOptions _options = new()
    {
        IsDynamicSettingStoreEnabled = true,
        CrossApplicationsCommonLockExpiration = TimeSpan.FromSeconds(42),
        CrossApplicationsCommonLockAcquireTimeout = TimeSpan.FromSeconds(7),
    };

    [Fact]
    public async Task should_acquire_the_common_lock_with_the_configured_timings_when_saving_changed_definitions()
    {
        // given: no stored hash, so the save path goes on to take the cross-application lock
        var lease = Substitute.For<IDistributedLease>();
        _distributedLockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(lease);
        _cache
            .GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(null, false));
        _staticStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _serializer.Serialize(Arg.Any<IEnumerable<SettingDefinition>>()).Returns([]);
        _repository.GetListAsync(Arg.Any<CancellationToken>()).Returns([]);
        using var sut = _CreateStore();

        // when
        await sut.SaveAsync(AbortToken);

        // then
        await _distributedLockProvider
            .Received(1)
            .TryAcquireAsync(
                _options.CrossApplicationsCommonLockKey,
                Arg.Is<DistributedLockAcquireOptions?>(o =>
                    o != null
                    && o.TimeUntilExpires == _options.CrossApplicationsCommonLockExpiration
                    && o.AcquireTimeout == _options.CrossApplicationsCommonLockAcquireTimeout
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_refresh_a_peer_when_a_seed_retry_repairs_the_stamp_after_the_rows_committed()
    {
        const string name = "App.Theme";
        var clock = new FakeTimeProvider();
        var record = new SettingDefinitionRecord(Guid.NewGuid(), name, name, null, "dark", null, false, true, false);
        var lease = Substitute.For<IDistributedLease>();
        _distributedLockProvider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions?>(), Arg.Any<CancellationToken>())
            .Returns(lease);
        _staticStore.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _serializer.Serialize(Arg.Any<IEnumerable<SettingDefinition>>()).Returns([record]);
        _serializer.Deserialize(record).Returns(new SettingDefinition(name, defaultValue: "dark"));

        var committed = false;
        _repository.GetListAsync(Arg.Any<CancellationToken>()).Returns(_ => committed ? [record] : []);
        _repository
            .When(x =>
                x.SaveAsync(
                    Arg.Any<List<SettingDefinitionRecord>>(),
                    Arg.Any<List<SettingDefinitionRecord>>(),
                    Arg.Any<List<SettingDefinitionRecord>>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => committed = true);

        var stamp = "old-stamp";
        _cache
            .GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(null, false));
        _cache
            .GetAsync<string>(_options.CommonSettingsUpdatedStampCacheKey, Arg.Any<CancellationToken>())
            .Returns(_ => new CacheValue<string>(stamp, true));
        var fail = true;
        _cache
            .UpsertAsync(
                _options.CommonSettingsUpdatedStampCacheKey,
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                AbortToken
            )
            .Returns(call =>
            {
                if (fail)
                {
                    fail = false;
                    throw new IOException("stamp write interrupted");
                }

                stamp = call.ArgAt<string>(1);
                return true;
            });

        using var seed = _CreateStore();
        using var peer = _CreateStore(clock);
        (await peer.GetOrDefaultAsync(name, AbortToken)).Should().BeNull();

        Func<Task> save = () => seed.SaveAsync(AbortToken);
        await save.Should().ThrowExactlyAsync<IOException>();
        await seed.SaveAsync(AbortToken);
        clock.Advance(_options.DynamicDefinitionsMemoryCacheExpiration + TimeSpan.FromTicks(1));

        var refreshed = await peer.GetOrDefaultAsync(name, AbortToken);
        (refreshed?.DefaultValue).Should().Be("dark");
    }

    private DynamicSettingDefinitionStore _CreateStore(FakeTimeProvider? clock = null)
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var application = Substitute.For<IHostIdentityAccessor>();
        application.ApplicationName.Returns("TestApp");

        return new DynamicSettingDefinitionStore(
            _repository,
            _staticStore,
            _serializer,
            _cache,
            _distributedLockProvider,
            guidGenerator,
            application,
            Options.Create(_options),
            Options.Create(new SettingManagementProvidersOptions()),
            clock ?? new FakeTimeProvider()
        );
    }
}
