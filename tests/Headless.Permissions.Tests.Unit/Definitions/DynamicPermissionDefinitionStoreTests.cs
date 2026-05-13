// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Definitions;

public sealed class DynamicPermissionDefinitionStoreTests : TestBase
{
    private readonly IPermissionDefinitionRecordRepository _repository;
    private readonly ICache _cache;
    private readonly IDistributedLockProvider _distributedLockProvider;
    private readonly PermissionManagementOptions _options;
    private readonly FakeTimeProvider _timeProvider;
    private readonly DynamicPermissionDefinitionStore _sut;

    public DynamicPermissionDefinitionStoreTests()
    {
        _repository = Substitute.For<IPermissionDefinitionRecordRepository>();
        var staticStore = Substitute.For<IStaticPermissionDefinitionStore>();
        var serializer = Substitute.For<IPermissionDefinitionSerializer>();
        _cache = Substitute.For<ICache>();
        _distributedLockProvider = Substitute.For<IDistributedLockProvider>();
        var messagePublisher = Substitute.For<IDirectPublisher>();
        var guidGenerator = Substitute.For<IGuidGenerator>();
        var application = Substitute.For<IApplicationInformationAccessor>();
        _options = new PermissionManagementOptions { IsDynamicPermissionStoreEnabled = true };
        var providersOptions = new PermissionManagementProvidersOptions();
        _timeProvider = new FakeTimeProvider();

        application.ApplicationName.Returns("TestApp");
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        var optionsAccessor = Substitute.For<IOptions<PermissionManagementOptions>>();
        optionsAccessor.Value.Returns(_options);

        var providersAccessor = Substitute.For<IOptions<PermissionManagementProvidersOptions>>();
        providersAccessor.Value.Returns(providersOptions);

        _sut = new DynamicPermissionDefinitionStore(
            _repository,
            staticStore,
            serializer,
            _cache,
            _distributedLockProvider,
            messagePublisher,
            guidGenerator,
            application,
            optionsAccessor,
            providersAccessor,
            _timeProvider
        );
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _sut.Dispose();
        return base.DisposeAsyncCore();
    }

    #region GetOrDefaultAsync

    [Fact]
    public async Task should_return_null_when_dynamic_store_disabled()
    {
        // given
        _options.IsDynamicPermissionStoreEnabled = false;

        // when
        var result = await _sut.GetOrDefaultAsync("SomePermission", AbortToken);

        // then
        result.Should().BeNull();
        await _repository.DidNotReceive().GetPermissionsListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_load_permissions_from_database()
    {
        // given
        const string permissionName = "Dynamic.Permission";
        var groupRecord = _CreateGroupRecord("TestGroup");
        var permissionRecord = _CreatePermissionRecord(permissionName, "TestGroup");

        _SetupCacheForNewStamp();
        _repository.GetGroupsListAsync(AbortToken).Returns([groupRecord]);
        _repository.GetPermissionsListAsync(AbortToken).Returns([permissionRecord]);

        // when
        var result = await _sut.GetOrDefaultAsync(permissionName, AbortToken);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().Be(permissionName);
    }

    [Fact]
    public async Task should_cache_dynamic_permissions_in_memory()
    {
        // given
        const string permissionName = "Dynamic.Permission";
        var groupRecord = _CreateGroupRecord("TestGroup");
        var permissionRecord = _CreatePermissionRecord(permissionName, "TestGroup");

        _SetupCacheForExistingStamp("existing-stamp");
        _repository.GetGroupsListAsync(AbortToken).Returns([groupRecord]);
        _repository.GetPermissionsListAsync(AbortToken).Returns([permissionRecord]);

        // first call to populate cache
        await _sut.GetOrDefaultAsync(permissionName, AbortToken);

        // when - second call should use memory cache
        var result = await _sut.GetOrDefaultAsync(permissionName, AbortToken);

        // then
        result.Should().NotBeNull();
        // Repository should only be called once during initial load
        await _repository.Received(1).GetPermissionsListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_refresh_cache_after_expiration()
    {
        // given
        const string permissionName = "Dynamic.Permission";
        var groupRecord = _CreateGroupRecord("TestGroup");
        var permissionRecord = _CreatePermissionRecord(permissionName, "TestGroup");
        var stamp1 = Guid.NewGuid().ToString("N");
        var stamp2 = Guid.NewGuid().ToString("N");

        _cache
            .GetAsync<string>(_options.CommonPermissionsUpdatedStampCacheKey, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(stamp1, true), new CacheValue<string>(stamp2, true));

        _repository.GetGroupsListAsync(Arg.Any<CancellationToken>()).Returns([groupRecord]);
        _repository.GetPermissionsListAsync(Arg.Any<CancellationToken>()).Returns([permissionRecord]);

        // First call
        await _sut.GetOrDefaultAsync(permissionName, AbortToken);

        // Advance time past cache expiration
        _timeProvider.Advance(_options.DynamicDefinitionsMemoryCacheExpiration + TimeSpan.FromSeconds(1));

        // when - second call after expiration
        await _sut.GetOrDefaultAsync(permissionName, AbortToken);

        // then - should check cache stamp again (but not necessarily reload from DB if stamp unchanged)
        await _cache
            .Received(2)
            .GetAsync<string>(_options.CommonPermissionsUpdatedStampCacheKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_deserialize_permission_properties_correctly()
    {
        // given
        const string permissionName = "Dynamic.Permission";
        var groupRecord = _CreateGroupRecord("TestGroup");
        groupRecord.ExtraProperties["CustomKey"] = "CustomValue";
        var permissionRecord = _CreatePermissionRecord(permissionName, "TestGroup");
        permissionRecord.ExtraProperties["PermKey"] = "PermValue";

        _SetupCacheForNewStamp();
        _repository.GetGroupsListAsync(AbortToken).Returns([groupRecord]);
        _repository.GetPermissionsListAsync(AbortToken).Returns([permissionRecord]);

        // when
        var result = await _sut.GetOrDefaultAsync(permissionName, AbortToken);

        // then
        result.Should().NotBeNull();
        result!["PermKey"].Should().Be("PermValue");
    }

    [Fact]
    public async Task should_handle_empty_database()
    {
        // given
        _SetupCacheForNewStamp();
        _repository.GetGroupsListAsync(AbortToken).Returns([]);
        _repository.GetPermissionsListAsync(AbortToken).Returns([]);

        // when
        var result = await _sut.GetOrDefaultAsync("NonExistent", AbortToken);

        // then
        result.Should().BeNull();
    }

    #endregion

    #region GetPermissionsAsync

    [Fact]
    public async Task should_return_empty_list_when_dynamic_store_disabled()
    {
        // given
        _options.IsDynamicPermissionStoreEnabled = false;

        // when
        var result = await _sut.GetPermissionsAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private void _SetupCacheForNewStamp()
    {
        var stamp = Guid.NewGuid().ToString("N");

        // Return null initially, then setup lock acquisition
        _cache
            .GetAsync<string>(_options.CommonPermissionsUpdatedStampCacheKey, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(null, false), new CacheValue<string>(stamp, true));

        var lockHandle = Substitute.For<IDistributedLock>();
        _distributedLockProvider
            .TryAcquireAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(lockHandle);
    }

    private void _SetupCacheForExistingStamp(string stamp)
    {
        _cache
            .GetAsync<string>(_options.CommonPermissionsUpdatedStampCacheKey, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>(stamp, true));
    }

    private static PermissionGroupDefinitionRecord _CreateGroupRecord(string name)
    {
        return new PermissionGroupDefinitionRecord(Guid.NewGuid(), name, name);
    }

    private static PermissionDefinitionRecord _CreatePermissionRecord(
        string name,
        string groupName,
        string? parentName = null
    )
    {
        return new PermissionDefinitionRecord(Guid.NewGuid(), groupName, name, parentName, name, isEnabled: true);
    }

    #endregion
}
