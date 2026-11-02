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

    private DynamicSettingDefinitionStore _CreateStore()
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
            new FakeTimeProvider()
        );
    }
}
