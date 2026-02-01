// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Tests.Grants;

public sealed class PermissionGrantStoreTests : TestBase
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "admin";

    private readonly IPermissionDefinitionManager _definitionManager;
    private readonly IPermissionGrantRepository _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICache<PermissionGrantCacheItem> _cache;
    private readonly ICurrentTenant _currentTenant;
    private readonly PermissionGrantStore _sut;

    public PermissionGrantStoreTests()
    {
        _definitionManager = Substitute.For<IPermissionDefinitionManager>();
        _repository = Substitute.For<IPermissionGrantRepository>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _cache = Substitute.For<ICache<PermissionGrantCacheItem>>();
        _currentTenant = Substitute.For<ICurrentTenant>();
        var logger = Substitute.For<ILogger<PermissionGrantStore>>();

        _sut = new PermissionGrantStore(
            _definitionManager,
            _repository,
            _guidGenerator,
            _cache,
            _currentTenant,
            logger
        );
    }

    #region IsGrantedAsync - Single

    [Fact]
    public async Task should_return_status_from_warm_cache()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: true);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Granted);
        await _repository.DidNotReceive().GetListAsync(_ProviderName, _ProviderKey, AbortToken);
    }

    [Fact]
    public async Task should_warm_cache_on_miss_and_return_status()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var permission = _CreatePermission(permissionName);
        var grantRecord = new PermissionGrantRecord(Guid.NewGuid(), permissionName, _ProviderName, _ProviderKey, true);

        _cache.GetAsync(cacheKey, AbortToken).Returns(CacheValue<PermissionGrantCacheItem>.NoValue);
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission]);
        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns([grantRecord]);

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Granted);
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(d => d.Count == 1),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_return_undefined_when_no_record_exists()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var permission = _CreatePermission(permissionName);

        _cache.GetAsync(cacheKey, AbortToken).Returns(CacheValue<PermissionGrantCacheItem>.NoValue);
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission]);
        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns([]);

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_return_prohibited_for_explicit_denial()
    {
        // given
        const string permissionName = "Users.Delete";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: false);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_return_undefined_for_null_cached_value()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: null);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Undefined);
    }

    #endregion

    #region IsGrantedAsync - Batch

    [Fact]
    public async Task should_optimize_single_item_batch()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: true);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync([permissionName], _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(1);
        result[permissionName].Should().Be(PermissionGrantStatus.Granted);
        await _cache.Received(1).GetAsync(cacheKey, AbortToken);
        await _cache.DidNotReceive().GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken);
    }

    [Fact]
    public async Task should_batch_check_multiple_permissions()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var cacheKey1 = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", _ProviderName, _ProviderKey);
        var cacheKey2 = PermissionGrantCacheItem.CalculateCacheKey("Users.Update", _ProviderName, _ProviderKey);

        var cacheResults = new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal)
        {
            [cacheKey1] = new(new PermissionGrantCacheItem(true), true),
            [cacheKey2] = new(new PermissionGrantCacheItem(false), true),
        };

        _cache.GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken).Returns(cacheResults);

        // when
        var result = await _sut.IsGrantedAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result["Users.Create"].Should().Be(PermissionGrantStatus.Granted);
        result["Users.Update"].Should().Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_warm_cache_for_missing_items_in_batch()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var cacheKey1 = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", _ProviderName, _ProviderKey);
        var cacheKey2 = PermissionGrantCacheItem.CalculateCacheKey("Users.Update", _ProviderName, _ProviderKey);

        var cacheResults = new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal)
        {
            [cacheKey1] = new(new PermissionGrantCacheItem(true), true),
            [cacheKey2] = CacheValue<PermissionGrantCacheItem>.NoValue,
        };

        var permission2 = _CreatePermission("Users.Update");
        var grantRecord = new PermissionGrantRecord(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, true);

        _cache.GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken).Returns(cacheResults);
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission2]);
        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([grantRecord]);

        // when
        var result = await _sut.IsGrantedAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        await _cache
            .Received(1)
            .UpsertAllAsync(Arg.Any<IDictionary<string, PermissionGrantCacheItem>>(), Arg.Any<TimeSpan>(), AbortToken);
    }

    #endregion

    #region GrantAsync - Single

    [Fact]
    public async Task should_insert_grant_when_no_record_exists()
    {
        // given
        const string permissionName = "Users.Create";
        var newId = Guid.NewGuid();
        const string tenantId = "tenant-1";

        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(newId);

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, tenantId, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertAsync(
                Arg.Is<PermissionGrantRecord>(r =>
                    r.Id == newId
                    && r.Name == permissionName
                    && r.ProviderName == _ProviderName
                    && r.ProviderKey == _ProviderKey
                    && r.IsGranted
                    && r.TenantId == tenantId
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_denied_record_to_granted()
    {
        // given
        const string permissionName = "Users.Create";
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var deniedRecord = new PermissionGrantRecord(
            existingId,
            permissionName,
            _ProviderName,
            _ProviderKey,
            false,
            "tenant-1"
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(deniedRecord);
        _guidGenerator.Create().Returns(newId);

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, "tenant-1", AbortToken);

        // then
        await _repository.Received(1).DeleteAsync(deniedRecord, AbortToken);
        await _repository.Received(1).InsertAsync(Arg.Is<PermissionGrantRecord>(r => r.IsGranted), AbortToken);
    }

    [Fact]
    public async Task should_skip_when_already_granted()
    {
        // given
        const string permissionName = "Users.Create";
        var grantedRecord = new PermissionGrantRecord(
            Guid.NewGuid(),
            permissionName,
            _ProviderName,
            _ProviderKey,
            true
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(grantedRecord);

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _repository.DidNotReceive().InsertAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
    }

    [Fact]
    public async Task should_update_cache_on_grant()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);

        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAsync(
                cacheKey,
                Arg.Is<PermissionGrantCacheItem>(c => c.IsGranted == true),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion

    #region GrantAsync - Batch

    [Fact]
    public async Task should_batch_grant_multiple_permissions()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        const string tenantId = "tenant-1";

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, tenantId, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertManyAsync(Arg.Is<IEnumerable<PermissionGrantRecord>>(records => records.Count() == 2), AbortToken);
    }

    [Fact]
    public async Task should_skip_batch_when_all_already_granted()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var existingRecords = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, true),
        };

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns(existingRecords);

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _repository.DidNotReceive().InsertManyAsync(Arg.Any<IEnumerable<PermissionGrantRecord>>(), AbortToken);
    }

    [Fact]
    public async Task should_update_cache_on_batch_grant()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(d =>
                    d.Count == 2 && d.Values.All(v => v.IsGranted == true)
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion

    #region RevokeAsync - Single

    [Fact]
    public async Task should_insert_denial_when_no_record_exists()
    {
        // given
        const string permissionName = "Users.Delete";
        var newId = Guid.NewGuid();
        const string tenantId = "tenant-1";

        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(newId);
        _currentTenant.Id.Returns(tenantId);

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertAsync(
                Arg.Is<PermissionGrantRecord>(r =>
                    r.Id == newId && r.Name == permissionName && !r.IsGranted && r.TenantId == tenantId
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_granted_record_to_denied()
    {
        // given
        const string permissionName = "Users.Create";
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var grantedRecord = new PermissionGrantRecord(
            existingId,
            permissionName,
            _ProviderName,
            _ProviderKey,
            true,
            "tenant-1"
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(grantedRecord);
        _guidGenerator.Create().Returns(newId);

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository.Received(1).DeleteAsync(grantedRecord, AbortToken);
        await _repository.Received(1).InsertAsync(Arg.Is<PermissionGrantRecord>(r => !r.IsGranted), AbortToken);
    }

    [Fact]
    public async Task should_skip_when_already_denied()
    {
        // given
        const string permissionName = "Users.Delete";
        var deniedRecord = new PermissionGrantRecord(
            Guid.NewGuid(),
            permissionName,
            _ProviderName,
            _ProviderKey,
            false
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(deniedRecord);

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository.DidNotReceive().InsertAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
    }

    [Fact]
    public async Task should_update_cache_on_revoke()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var grantedRecord = new PermissionGrantRecord(
            Guid.NewGuid(),
            permissionName,
            _ProviderName,
            _ProviderKey,
            true
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(grantedRecord);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAsync(
                cacheKey,
                Arg.Is<PermissionGrantCacheItem>(c => c.IsGranted == false),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion

    #region RevokeAsync - Batch

    [Fact]
    public async Task should_batch_revoke_multiple_permissions()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var existingRecords = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, true),
        };

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns(existingRecords);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.RevokeAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .DeleteManyAsync(
                Arg.Is<IReadOnlyCollection<PermissionGrantRecord>>(r => r.Count == 2 && r.All(x => x.IsGranted)),
                AbortToken
            );
        await _repository
            .Received(1)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(records => records.All(r => !r.IsGranted)),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_cache_on_batch_revoke()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());
        _currentTenant.Id.Returns("tenant-1");

        // when
        await _sut.RevokeAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(d =>
                    d.Count == 2 && d.Values.All(v => v.IsGranted == false)
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion

    #region GetAllGrantsAsync

    [Fact]
    public async Task should_return_all_grants_from_repository()
    {
        // given
        var records = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Delete", _ProviderName, _ProviderKey, false),
        };

        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns(records);

        // when
        var result = await _sut.GetAllGrantsAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(g => g.Name == "Users.Create" && g.IsGranted);
        result.Should().Contain(g => g.Name == "Users.Delete" && !g.IsGranted);
    }

    #endregion

    #region Helpers

    private static PermissionDefinition _CreatePermission(string name, bool isEnabled = true)
    {
        var ctor = typeof(PermissionDefinition).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(string), typeof(bool)],
            null
        );

        return (PermissionDefinition)ctor!.Invoke([name, name, isEnabled]);
    }

    #endregion
}
